using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GUNRPG.Application.Dtos;
using Hex1b;
using Hex1b.Widgets;

var baseAddress = Environment.GetEnvironmentVariable("GUNRPG_API_BASE") ?? "http://localhost:5209";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.Converters.Add(new JsonStringEnumConverter());

using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
using var cts = new CancellationTokenSource();

var gameState = new GameState(httpClient, jsonOptions);

// Try to auto-load last used operator
gameState.LoadSavedOperatorId();
if (gameState.CurrentOperatorId.HasValue && gameState.CurrentOperator != null)
{
    gameState.CurrentScreen = Screen.BaseCamp;
}

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var app = new Hex1bApp(_ => gameState.BuildUI(cts));
await app.RunAsync(cts.Token);

class GameState(HttpClient client, JsonSerializerOptions options)
{
    public Screen CurrentScreen { get; set; } = Screen.MainMenu;
    public Screen ReturnScreen { get; set; } = Screen.MainMenu;
    
    public Guid? CurrentOperatorId { get; set; }
    public OperatorState? CurrentOperator { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public CombatSessionDto? CurrentSession { get; set; }
    public List<OperatorSummary>? AvailableOperators { get; set; }
    
    public string OperatorName { get; set; } = "";
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    public Task<Hex1bWidget> BuildUI(CancellationTokenSource cts)
    {
        return CurrentScreen switch
        {
            Screen.MainMenu => Task.FromResult<Hex1bWidget>(BuildMainMenu(cts)),
            Screen.SelectOperator => Task.FromResult<Hex1bWidget>(BuildSelectOperator()),
            Screen.CreateOperator => Task.FromResult<Hex1bWidget>(BuildCreateOperator()),
            Screen.BaseCamp => Task.FromResult<Hex1bWidget>(BuildBaseCamp()),
            Screen.StartMission => Task.FromResult<Hex1bWidget>(BuildStartMission()),
            Screen.CombatSession => Task.FromResult<Hex1bWidget>(BuildCombatSession()),
            Screen.MissionComplete => Task.FromResult<Hex1bWidget>(BuildMissionComplete()),
            Screen.Message => Task.FromResult<Hex1bWidget>(BuildMessage()),
            _ => Task.FromResult<Hex1bWidget>(new TextBlockWidget("Unknown screen"))
        };
    }

    Hex1bWidget BuildMainMenu(CancellationTokenSource cts)
    {
        var menuItems = new[] {
            "CREATE NEW OPERATOR",
            "SELECT OPERATOR",
            "EXIT"
        };

        return new VStackWidget([
            UI.CreateBorder("GUNRPG - OPERATOR TERMINAL"),
            new TextBlockWidget(""),
            UI.CreateBorder("MAIN MENU", new VStackWidget([
                new TextBlockWidget("  Select an option:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // CREATE NEW OPERATOR
                            CurrentScreen = Screen.CreateOperator;
                            OperatorName = "";
                            break;
                        case 1: // SELECT OPERATOR
                            ErrorMessage = null;
                            LoadOperatorList();
                            CurrentScreen = Screen.SelectOperator;
                            break;
                        case 2: // EXIT
                            cts.Cancel();
                            break;
                    }
                })
            ])),
            new TextBlockWidget(""),
            UI.CreateStatusBar($"API: {client.BaseAddress}"),
        ]);
    }

    void LoadOperatorList()
    {
        try
        {
            var response = client.GetAsync("operators").GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var operators = response.Content.ReadFromJsonAsync<List<JsonElement>>(options).GetAwaiter().GetResult();
                AvailableOperators = operators?.Select(ParseOperatorSummary).ToList();
            }
            else
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"{response.StatusCode}: {errorContent}";
                AvailableOperators = new List<OperatorSummary>();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Exception: {ex.Message}";
            AvailableOperators = new List<OperatorSummary>();
        }
    }

    Hex1bWidget BuildSelectOperator()
    {
        var contentWidgets = new List<Hex1bWidget>
        {
            new TextBlockWidget("  Available Operators:"),
            new TextBlockWidget("")
        };

        if (ErrorMessage != null)
        {
            contentWidgets.Add(new TextBlockWidget($"  ERROR: {ErrorMessage}"));
            contentWidgets.Add(new TextBlockWidget(""));
        }

        if (AvailableOperators == null || AvailableOperators.Count == 0)
        {
            contentWidgets.Add(new TextBlockWidget("  No operators found."));
            contentWidgets.Add(new TextBlockWidget("  Create one from the main menu."));
            contentWidgets.Add(new TextBlockWidget(""));
            contentWidgets.Add(new ListWidget(new[] { "BACK TO MAIN MENU" })
                .OnItemActivated(_ => CurrentScreen = Screen.MainMenu));
        }
        else
        {
            var operatorItems = AvailableOperators.Select(op => {
                var status = op.IsDead ? "KIA" : op.CurrentMode;
                var healthPct = op.MaxHealth > 0 ? (int)(100 * op.CurrentHealth / op.MaxHealth) : 0;
                return $"{op.Name} - {status} (HP: {healthPct}%, XP: {op.TotalXp})";
            }).Concat(new[] { "--- BACK TO MAIN MENU ---" }).ToArray();

            contentWidgets.Add(new ListWidget(operatorItems).OnItemActivated(e => {
                if (e.ActivatedIndex == AvailableOperators.Count)
                {
                    // Back to main menu
                    CurrentScreen = Screen.MainMenu;
                }
                else
                {
                    SelectOperator(AvailableOperators[e.ActivatedIndex].Id);
                }
            }));
        }

        return new VStackWidget([
            UI.CreateBorder("SELECT OPERATOR"),
            new TextBlockWidget(""),
            UI.CreateBorder("OPERATOR LIST", new VStackWidget(contentWidgets)),
            UI.CreateStatusBar("Choose an operator to continue")
        ]);
    }

    void SelectOperator(Guid operatorId)
    {
        try
        {
            CurrentOperatorId = operatorId;
            SaveCurrentOperatorId();
            LoadOperator(operatorId);
            CurrentScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Failed to load operator.\nError: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.SelectOperator;
        }
    }

    void LoadOperator(Guid operatorId)
    {
        var response = client.GetAsync($"operators/{operatorId}").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to load operator: {response.StatusCode}");
        }

        var operatorDto = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
        CurrentOperator = ParseOperator(operatorDto);
    }

    void SaveCurrentOperatorId()
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configDir = Path.Combine(homeDir, ".gunrpg");
            Directory.CreateDirectory(configDir);
            var configFile = Path.Combine(configDir, "current_operator.txt");
            File.WriteAllText(configFile, CurrentOperatorId?.ToString() ?? "");
        }
        catch
        {
            // Silently fail - not critical
        }
    }

    public void LoadSavedOperatorId()
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configFile = Path.Combine(homeDir, ".gunrpg", "current_operator.txt");
            if (File.Exists(configFile))
            {
                var idText = File.ReadAllText(configFile).Trim();
                if (Guid.TryParse(idText, out var operatorId))
                {
                    CurrentOperatorId = operatorId;
                    LoadOperator(operatorId);
                }
            }
        }
        catch
        {
            // Silently fail - not critical
        }
    }

    Hex1bWidget BuildCreateOperator()
    {
        var contentWidgets = new List<Hex1bWidget>
        {
            new TextBlockWidget("  Enter operator name:"),
            new TextBlockWidget(""),
        };

        if (ErrorMessage != null)
        {
            contentWidgets.Add(new TextBlockWidget($"  ERROR: {ErrorMessage}"));
            contentWidgets.Add(new TextBlockWidget(""));
        }

        // Add text input using TextBoxWidget with OnTextChanged event handler
        var textBox = new TextBoxWidget(OperatorName ?? "")
            .OnTextChanged(args => {
                OperatorName = args.NewText;
                ErrorMessage = null; // Clear error when user types
            });
        contentWidgets.Add(textBox);
        contentWidgets.Add(new TextBlockWidget(""));
        contentWidgets.Add(new TextBlockWidget($"  Current: {(string.IsNullOrWhiteSpace(OperatorName) ? "(empty)" : OperatorName)}"));
        contentWidgets.Add(new TextBlockWidget(""));
        
        // Action menu using ListWidget
        var actionItems = new[] {
            "GENERATE RANDOM NAME",
            "CREATE",
            "BACK"
        };
        
        contentWidgets.Add(new ListWidget(actionItems).OnItemActivated(e => {
            switch (e.ActivatedIndex)
            {
                case 0: // Generate Random Name
                    OperatorName = $"Operative-{Random.Shared.Next(1000, 9999)}";
                    break;
                case 1: // Create
                    // NOTE: CreateOperator blocks on HTTP calls due to hex1b's synchronous event handlers.
                    // This is a known limitation. UI will freeze during API calls.
                    CreateOperator();
                    break;
                case 2: // Back
                    CurrentScreen = Screen.MainMenu;
                    OperatorName = "";
                    break;
            }
        }));
        
        return new VStackWidget([
            UI.CreateBorder("CREATE NEW OPERATOR"),
            new TextBlockWidget(""),
            UI.CreateBorder("OPERATOR PROFILE", new VStackWidget(contentWidgets)),
            UI.CreateStatusBar("Type name or generate random, then CREATE")
        ]);
    }

    void CreateOperator()
    {
        if (string.IsNullOrWhiteSpace(OperatorName))
        {
            ErrorMessage = "Name cannot be empty";
            return;
        }

        try
        {
            // NOTE: Using GetAwaiter().GetResult() here because hex1b's ButtonWidget.OnClick
            // handlers are synchronous (Action<MouseEvent>). This is a known limitation of the
            // hex1b library. In a real application, consider using a different UI framework
            // with async event handler support.
            var request = new { Name = OperatorName };
            var response = client.PostAsJsonAsync("operators", request, options).GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed: {response.StatusCode}";
                return;
            }

            var operatorDto = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
            CurrentOperatorId = operatorDto.GetProperty("id").GetGuid();
            CurrentOperator = ParseOperator(operatorDto);
            SaveCurrentOperatorId();
            ErrorMessage = null;
            CurrentScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    Hex1bWidget BuildBaseCamp()
    {
        var op = CurrentOperator;
        if (op == null)
        {
            return new TextBlockWidget("No operator loaded");
        }

        var menuItems = new List<string>();
        
        if (op.CurrentMode == "Base")
        {
            menuItems.Add("START MISSION");
            menuItems.Add("VIEW STATS");
        }
        else
        {
            menuItems.Add("CONTINUE MISSION");
            menuItems.Add("VIEW STATS");
        }
        
        menuItems.Add("MAIN MENU");

        var menuWidget = new ListWidget(menuItems.ToArray()).OnItemActivated(e => {
            if (op.CurrentMode == "Base")
            {
                switch (e.ActivatedIndex)
                {
                    case 0: // START MISSION
                        CurrentScreen = Screen.StartMission;
                        break;
                    case 1: // VIEW STATS
                        Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nPerks: {string.Join(", ", op.UnlockedPerks)}\n\nPress OK to continue.";
                        CurrentScreen = Screen.Message;
                        ReturnScreen = Screen.BaseCamp;
                        break;
                    case 2: // MAIN MENU
                        CurrentScreen = Screen.MainMenu;
                        break;
                }
            }
            else
            {
                switch (e.ActivatedIndex)
                {
                    case 0: // CONTINUE MISSION
                        if (op.ActiveSessionId.HasValue)
                        {
                            ActiveSessionId = op.ActiveSessionId;
                            LoadSession();
                        }
                        break;
                    case 1: // VIEW STATS
                        Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nMission In Progress\n\nPress OK to continue.";
                        CurrentScreen = Screen.Message;
                        ReturnScreen = Screen.BaseCamp;
                        break;
                    case 2: // MAIN MENU
                        CurrentScreen = Screen.MainMenu;
                        break;
                }
            }
        });

        var healthBar = UI.CreateProgressBar("HP", (int)op.CurrentHealth, (int)op.MaxHealth, 30);
        var xpInfo = $"XP: {op.TotalXp}  STREAK: {op.ExfilStreak}";
        
        return new VStackWidget([
            UI.CreateBorder($"OPERATOR: {op.Name.ToUpper()}"),
            new TextBlockWidget(""),
            new HStackWidget([
                UI.CreateBorder("STATUS", new VStackWidget([
                    new TextBlockWidget("  "),
                    healthBar,
                    new TextBlockWidget(""),
                    new TextBlockWidget($"  {xpInfo}"),
                    new TextBlockWidget($"  WEAPON: {op.EquippedWeaponName}"),
                    new TextBlockWidget($"  MODE: {op.CurrentMode}"),
                    new TextBlockWidget($"  PERKS: {op.UnlockedPerks.Count}"),
                    op.IsDead ? new TextBlockWidget("  STATUS: KIA") : new TextBlockWidget("")
                ])),
                new TextBlockWidget("  "),
                UI.CreateBorder("BASE CAMP", menuWidget)
            ]),
            new TextBlockWidget(""),
            UI.CreateStatusBar($"Operator ID: {op.Id}")
        ]);
    }

    Hex1bWidget BuildStartMission()
    {
        var menuItems = new[] {
            "BEGIN INFILTRATION",
            "CANCEL"
        };

        return new VStackWidget([
            UI.CreateBorder("MISSION BRIEFING"),
            new TextBlockWidget(""),
            UI.CreateBorder("INFILTRATION", new VStackWidget([
                new TextBlockWidget("  OBJECTIVE: Engage hostile target"),
                new TextBlockWidget("  TIME LIMIT: 30 minutes"),
                new TextBlockWidget("  THREAT LEVEL: Variable"),
                new TextBlockWidget(""),
                new TextBlockWidget("  WARNING: Death is permanent."),
                new TextBlockWidget("           Health does not regenerate."),
                new TextBlockWidget(""),
                new TextBlockWidget("  Select action:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // BEGIN INFILTRATION
                            // NOTE: StartMission blocks on HTTP calls due to hex1b's synchronous event handlers.
                            // This is a known limitation. UI will freeze during API calls.
                            StartMission();
                            break;
                        case 1: // CANCEL
                            CurrentScreen = Screen.BaseCamp;
                            break;
                    }
                })
            ])),
            UI.CreateStatusBar("Prepare for combat")
        ]);
    }

    void StartMission()
    {
        try
        {
            // Step 1: Start the infil (locks operator in Infil mode)
            var response = client.PostAsync($"operators/{CurrentOperatorId}/infil/start", null).GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to start mission: {response.StatusCode}";
                Message = $"Mission start failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            var result = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
            ActiveSessionId = result.GetProperty("sessionId").GetGuid();
            CurrentOperator = ParseOperator(result.GetProperty("operator"));
            
            // Step 2: Create the combat session with the session ID from infil
            var sessionRequest = new
            {
                id = ActiveSessionId,
                operatorId = CurrentOperatorId,
                playerName = CurrentOperator!.Name,
                weaponName = CurrentOperator.EquippedWeaponName,
                playerLevel = 1,
                playerMaxHealth = CurrentOperator.MaxHealth,
                playerCurrentHealth = CurrentOperator.CurrentHealth
            };
            
            var sessionResponse = client.PostAsJsonAsync("sessions", sessionRequest, options).GetAwaiter().GetResult();
            if (!sessionResponse.IsSuccessStatusCode)
            {
                var errorContent = sessionResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to create combat session: {sessionResponse.StatusCode} - {errorContent}";
                Message = $"Combat session creation failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                
                // If session creation failed, try to abort the infil to reset operator state
                if (CurrentOperatorId.HasValue)
                {
                    try
                    {
                        var abortRequest = new { SessionId = ActiveSessionId };
                        client.PostAsJsonAsync($"operators/{CurrentOperatorId.Value}/infil/outcome", abortRequest).GetAwaiter().GetResult();
                        LoadOperator(CurrentOperatorId.Value);  // Reload operator
                        ActiveSessionId = null;
                    }
                    catch
                    {
                        // Silently fail - user will see the original error
                    }
                }
                return;
            }
            
            // Verify session was created successfully by checking if we can retrieve it
            LoadSession();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    void LoadSession()
    {
        try
        {
            var sessionData = client.GetFromJsonAsync<CombatSessionDto>($"sessions/{ActiveSessionId}/state", options).GetAwaiter().GetResult();
            if (sessionData != null)
            {
                CurrentSession = sessionData;
                CurrentScreen = Screen.CombatSession;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            // Session doesn't exist - this can happen if operator was created before session creation was implemented
            // Force end the infil by processing a failed outcome to reset the operator to Base mode
            ErrorMessage = "Combat session not found - forcing mission abort";
            Message = $"Mission session not found in database.\n\nThis can happen with operators created before the latest updates.\nForcing mission abort to reset operator state.\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
            
            // Try to force-end the infil by processing a "died" outcome
            if (CurrentOperator?.Id != null)
            {
                try
                {
                    var request = new { SessionId = ActiveSessionId };
                    client.PostAsJsonAsync($"operators/{CurrentOperator.Id}/infil/outcome", request).GetAwaiter().GetResult();
                    LoadOperator(CurrentOperator.Id);  // Reload operator to get updated state
                }
                catch
                {
                    // If that fails, just clear the local session ID
                    ActiveSessionId = null;
                }
            }
            else
            {
                ActiveSessionId = null;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Failed to load session.\nError: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    Hex1bWidget BuildCombatSession()
    {
        var session = CurrentSession;
        if (session == null)
        {
            return new TextBlockWidget("No session loaded");
        }

        var player = session.Player;
        var enemy = session.Enemy;
        
        var playerHpBar = UI.CreateProgressBar("HP", (int)player.Health, (int)player.MaxHealth, 20);
        var enemyHpBar = UI.CreateProgressBar("HP", (int)enemy.Health, (int)enemy.MaxHealth, 20);
        
        var actionItems = new[] {
            "SUBMIT INTENTS (Not Implemented)",
            "ADVANCE TURN",
            "VIEW DETAILS",
            "RETURN TO BASE"
        };

        return new VStackWidget([
            UI.CreateBorder("COMBAT SESSION"),
            new TextBlockWidget(""),
            new HStackWidget([
                UI.CreateBorder("PLAYER", new VStackWidget([
                    new TextBlockWidget($"  {player.Name}"),
                    new TextBlockWidget("  "),
                    playerHpBar,
                    new TextBlockWidget($"  AMMO: {player.CurrentAmmo}/{player.MagazineSize}"),
                    new TextBlockWidget($"  COVER: {player.CurrentCover}"),
                    new TextBlockWidget($"  MOVE: {player.CurrentMovement}")
                ])),
                new TextBlockWidget("  "),
                UI.CreateBorder("ENEMY", new VStackWidget([
                    new TextBlockWidget($"  {enemy.Name} (LVL {session.EnemyLevel})"),
                    new TextBlockWidget("  "),
                    enemyHpBar,
                    new TextBlockWidget($"  AMMO: {enemy.CurrentAmmo}/{enemy.MagazineSize}"),
                    new TextBlockWidget($"  DIST: {player.DistanceToOpponent:F1}m")
                ]))
            ]),
            new TextBlockWidget(""),
            UI.CreateBorder("ACTIONS", new VStackWidget([
                new TextBlockWidget($"  TURN: {session.TurnNumber}  PHASE: {session.Phase}  TIME: {session.CurrentTimeMs}ms"),
                new TextBlockWidget(""),
                new ListWidget(actionItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // SUBMIT INTENTS
                            Message = "Intent submission not yet implemented in Pokemon UI.\nUse API directly for now.\n\nPress OK to continue.";
                            CurrentScreen = Screen.Message;
                            ReturnScreen = Screen.CombatSession;
                            break;
                        case 1: // ADVANCE TURN
                            // NOTE: AdvanceCombat blocks on HTTP calls due to hex1b's synchronous event handlers.
                            // This is a known limitation. UI will freeze during API calls.
                            AdvanceCombat();
                            break;
                        case 2: // VIEW DETAILS
                            var pet = session.Pet;
                            Message = $"Combat Details:\n\nPlayer: {session.Player.Name}\nHealth: {session.Player.Health:F0}/{session.Player.MaxHealth:F0}\nAmmo: {session.Player.CurrentAmmo}/{session.Player.MagazineSize}\n\nEnemy: {session.Enemy.Name}\nHealth: {session.Enemy.Health:F0}/{session.Enemy.MaxHealth:F0}\n\nPet Health: {pet.Health:F0}\nPet Morale: {pet.Morale:F0}\n\nPress OK to continue.";
                            CurrentScreen = Screen.Message;
                            ReturnScreen = Screen.CombatSession;
                            break;
                        case 3: // RETURN TO BASE
                            CurrentScreen = Screen.BaseCamp;
                            RefreshOperator();
                            break;
                    }
                })
            ])),
            UI.CreateStatusBar($"Session: {session.Id}")
        ]);
    }

    void AdvanceCombat()
    {
        try
        {
            var response = client.PostAsync($"sessions/{ActiveSessionId}/advance", null).GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Advance failed: {response.StatusCode}";
                return;
            }

            var sessionData = response.Content.ReadFromJsonAsync<CombatSessionDto>(options).GetAwaiter().GetResult();
            if (sessionData != null)
            {
                CurrentSession = sessionData;
                
                if (sessionData.Player.Health <= 0 || sessionData.Enemy.Health <= 0)
                {
                    // Combat has ended - process the outcome
                    ProcessCombatOutcome();
                    
                    Message = sessionData.Player.Health <= 0 
                        ? "MISSION FAILED\n\nYou were eliminated.\n\nPress OK to continue."
                        : "MISSION SUCCESS\n\nTarget eliminated.\n\nPress OK to continue.";
                    CurrentScreen = Screen.MissionComplete;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    void ProcessCombatOutcome()
    {
        try
        {
            // NOTE: Using empty request body - server will load session and compute outcome
            var request = new { SessionId = ActiveSessionId };
            var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/infil/outcome", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to process outcome: {response.StatusCode}";
                return;
            }

            // Refresh operator state to reflect outcome (XP, mode change, etc.)
            RefreshOperator();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Outcome processing error: {ex.Message}";
        }
    }

    void RefreshOperator()
    {
        try
        {
            var response = client.GetAsync($"operators/{CurrentOperatorId}").GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var operatorDto = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
                CurrentOperator = ParseOperator(operatorDto);
            }
        }
        catch (Exception)
        {
            // Silently fail - operator state will be stale but UI remains functional
        }
    }

    Hex1bWidget BuildMissionComplete()
    {
        return new VStackWidget([
            UI.CreateBorder("MISSION COMPLETE"),
            new TextBlockWidget(""),
            UI.CreateBorder("DEBRIEFING", new VStackWidget([
                new TextBlockWidget(""),
                new TextBlockWidget(Message ?? "Mission ended."),
                new TextBlockWidget(""),
                new ListWidget(new[] { "RETURN TO BASE" }).OnItemActivated(_ => CurrentScreen = Screen.BaseCamp)
            ])),
            UI.CreateStatusBar("Mission complete")
        ]);
    }

    Hex1bWidget BuildMessage()
    {
        var lines = Message?.Split('\n') ?? [""];
        var contentWidgets = new List<Hex1bWidget>();
        
        foreach (var line in lines)
        {
            contentWidgets.Add(new TextBlockWidget($"  {line}"));
        }
        
        contentWidgets.Add(new TextBlockWidget(""));
        contentWidgets.Add(new ListWidget(new[] { "OK" }).OnItemActivated(_ => CurrentScreen = ReturnScreen));

        return new VStackWidget([
            UI.CreateBorder("MESSAGE"),
            new TextBlockWidget(""),
            UI.CreateBorder("INFO", new VStackWidget(contentWidgets)),
            UI.CreateStatusBar("Press OK to continue")
        ]);
    }

    static OperatorState ParseOperator(JsonElement json)
    {
        return new OperatorState
        {
            Id = json.GetProperty("id").GetGuid(),
            Name = json.GetProperty("name").GetString() ?? "",
            TotalXp = json.GetProperty("totalXp").GetInt64(),
            CurrentHealth = json.GetProperty("currentHealth").GetSingle(),
            MaxHealth = json.GetProperty("maxHealth").GetSingle(),
            EquippedWeaponName = json.GetProperty("equippedWeaponName").GetString() ?? "",
            UnlockedPerks = json.GetProperty("unlockedPerks").EnumerateArray().Select(p => p.GetString() ?? "").ToList(),
            ExfilStreak = json.GetProperty("exfilStreak").GetInt32(),
            IsDead = json.GetProperty("isDead").GetBoolean(),
            CurrentMode = json.GetProperty("currentMode").GetString() ?? "Base",
            InfilStartTime = json.TryGetProperty("infilStartTime", out var time) && time.ValueKind != JsonValueKind.Null 
                ? time.GetDateTimeOffset() 
                : null,
            ActiveSessionId = json.TryGetProperty("activeSessionId", out var sid) && sid.ValueKind != JsonValueKind.Null 
                ? sid.GetGuid() 
                : null,
            LockedLoadout = json.GetProperty("lockedLoadout").GetString() ?? ""
        };
    }

    static OperatorSummary ParseOperatorSummary(JsonElement json)
    {
        return new OperatorSummary
        {
            Id = json.GetProperty("id").GetGuid(),
            Name = json.GetProperty("name").GetString() ?? "",
            CurrentMode = json.GetProperty("currentMode").GetString() ?? "Base",
            IsDead = json.GetProperty("isDead").GetBoolean(),
            TotalXp = json.GetProperty("totalXp").GetInt64(),
            CurrentHealth = json.GetProperty("currentHealth").GetSingle(),
            MaxHealth = json.GetProperty("maxHealth").GetSingle()
        };
    }
}

static class UI
{
    /// <summary>
    /// Creates a bordered panel with a title.
    /// Uses Hex1b's BorderWidget to properly render box-drawing borders.
    /// </summary>
    public static Hex1bWidget CreateBorder(string title, Hex1bWidget? content = null)
    {
        // If no content provided, use a simple text block for spacing
        var borderContent = content ?? new TextBlockWidget("");
        
        // Use BorderWidget with title as second parameter (hex1b 0.76.0 API)
        return new BorderWidget(borderContent, title);
    }

    /// <summary>
    /// Creates a simple status bar at the bottom of the screen.
    /// </summary>
    public static Hex1bWidget CreateStatusBar(string text)
    {
        return new TextBlockWidget($"  {text}");
    }

    /// <summary>
    /// Creates a visual progress bar widget with label.
    /// Uses hex1b's ProgressWidget for proper rendering.
    /// </summary>
    public static Hex1bWidget CreateProgressBar(string label, int current, int max, int width = 20)
    {
        // Calculate percentage for 0-100 range and clamp to valid range
        var percentage = max > 0 ? (int)((float)current / max * 100) : 0;
        percentage = Math.Clamp(percentage, 0, 100);
        
        var progressWidget = new ProgressWidget
        {
            Value = percentage
        };
        
        return new HStackWidget([
            new TextBlockWidget($"{label}: "),
            progressWidget.FixedWidth(width),
            new TextBlockWidget($" {current}/{max}")
        ]);
    }
}

class OperatorState
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
    public string EquippedWeaponName { get; init; } = "";
    public List<string> UnlockedPerks { get; init; } = new();
    public int ExfilStreak { get; init; }
    public bool IsDead { get; init; }
    public string CurrentMode { get; init; } = "Base";
    public DateTimeOffset? InfilStartTime { get; init; }
    public Guid? ActiveSessionId { get; init; }
    public string LockedLoadout { get; init; } = "";
}

class OperatorSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string CurrentMode { get; init; } = "Base";
    public bool IsDead { get; init; }
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
}

enum Screen
{
    MainMenu,
    SelectOperator,
    CreateOperator,
    BaseCamp,
    StartMission,
    CombatSession,
    MissionComplete,
    Message
}
