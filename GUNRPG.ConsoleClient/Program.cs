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
        return new VStackWidget([
            UI.CreateBorder("GUNRPG - OPERATOR TERMINAL", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("MAIN MENU", 50, 15, [
                new TextBlockWidget("  Select an option:"),
                new TextBlockWidget(""),
                new ButtonWidget("  ► CREATE NEW OPERATOR").OnClick(_ => {
                    CurrentScreen = Screen.CreateOperator;
                    OperatorName = "";
                }),
                new ButtonWidget("  ► SELECT OPERATOR").OnClick(_ => {
                    LoadOperatorList();
                    CurrentScreen = Screen.SelectOperator;
                }),
                new ButtonWidget("  ► EXIT").OnClick(_ => cts.Cancel())
            ]),
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
                ErrorMessage = $"Failed to load operators: {response.StatusCode}";
                AvailableOperators = new List<OperatorSummary>();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AvailableOperators = new List<OperatorSummary>();
        }
    }

    Hex1bWidget BuildSelectOperator()
    {
        var widgets = new List<Hex1bWidget>
        {
            new TextBlockWidget("  Available Operators:"),
            new TextBlockWidget("")
        };

        if (AvailableOperators == null || AvailableOperators.Count == 0)
        {
            widgets.Add(new TextBlockWidget("  No operators found."));
            widgets.Add(new TextBlockWidget("  Create one from the main menu."));
        }
        else
        {
            foreach (var op in AvailableOperators)
            {
                var status = op.IsDead ? "KIA" : op.CurrentMode;
                var healthPct = op.MaxHealth > 0 ? (int)(100 * op.CurrentHealth / op.MaxHealth) : 0;
                widgets.Add(new ButtonWidget($"  ► {op.Name} - {status} (HP: {healthPct}%, XP: {op.TotalXp})").OnClick(_ => {
                    SelectOperator(op.Id);
                }));
            }
        }

        widgets.Add(new TextBlockWidget(""));
        widgets.Add(new ButtonWidget("  ► BACK TO MAIN MENU").OnClick(_ => CurrentScreen = Screen.MainMenu));

        if (ErrorMessage != null)
        {
            widgets.Insert(1, new TextBlockWidget($"  ERROR: {ErrorMessage}"));
            widgets.Insert(2, new TextBlockWidget(""));
        }

        return new VStackWidget([
            UI.CreateBorder("SELECT OPERATOR", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("OPERATOR LIST", 70, 28, widgets),
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
        var widgets = new List<Hex1bWidget>
        {
            new TextBlockWidget("  Enter operator name:"),
            new TextBlockWidget(""),
        };

        // Add text input using TextBoxWidget with OnTextChanged event handler
        var textBox = new TextBoxWidget(OperatorName ?? "")
            .OnTextChanged(args => {
                OperatorName = args.NewText;
                ErrorMessage = null; // Clear error when user types
            });
        widgets.Add(textBox);
        widgets.Add(new TextBlockWidget(""));
        widgets.Add(new TextBlockWidget($"  Current: {(string.IsNullOrWhiteSpace(OperatorName) ? "(empty)" : OperatorName)}"));
        widgets.Add(new TextBlockWidget(""));
        
        // Action buttons
        widgets.Add(new ButtonWidget("  ► GENERATE RANDOM NAME").OnClick(_ => {
            OperatorName = $"Operative-{Random.Shared.Next(1000, 9999)}";
        }));
        widgets.Add(new ButtonWidget("  ► CREATE").OnClick(_ => CreateOperator()));
        widgets.Add(new ButtonWidget("  ► BACK").OnClick(_ => {
            CurrentScreen = Screen.MainMenu;
            OperatorName = "";
        }));

        if (ErrorMessage != null)
        {
            widgets.Insert(1, new TextBlockWidget($"  ERROR: {ErrorMessage}"));
        }
        
        return new VStackWidget([
            UI.CreateBorder("CREATE NEW OPERATOR", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("OPERATOR PROFILE", 60, 28, widgets),
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

        var menuWidgets = new List<Hex1bWidget>
        {
            new TextBlockWidget("  Select action:"),
            new TextBlockWidget("")
        };

        if (op.CurrentMode == "Base")
        {
            menuWidgets.Add(new ButtonWidget("  ► START MISSION").OnClick(_ => CurrentScreen = Screen.StartMission));
            menuWidgets.Add(new ButtonWidget("  ► VIEW STATS").OnClick(_ => {
                Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nPerks: {string.Join(", ", op.UnlockedPerks)}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
            }));
        }
        else
        {
            menuWidgets.Add(new ButtonWidget("  ► CONTINUE MISSION").OnClick(_ => {
                if (op.ActiveSessionId.HasValue)
                {
                    ActiveSessionId = op.ActiveSessionId;
                    LoadSession();
                }
            }));
            menuWidgets.Add(new ButtonWidget("  ► VIEW STATS").OnClick(_ => {
                Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nMission In Progress\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
            }));
        }
        
        menuWidgets.Add(new ButtonWidget("  ► MAIN MENU").OnClick(_ => CurrentScreen = Screen.MainMenu));

        var healthBar = UI.CreateBar("HP", (int)op.CurrentHealth, (int)op.MaxHealth, 30);
        var xpInfo = $"XP: {op.TotalXp}  STREAK: {op.ExfilStreak}";
        
        return new VStackWidget([
            UI.CreateBorder($"OPERATOR: {op.Name.ToUpper()}", 78, 5),
            new TextBlockWidget(""),
            new HStackWidget([
                UI.CreateBorder("STATUS", 38, 12, [
                    new TextBlockWidget($"  {healthBar}"),
                    new TextBlockWidget(""),
                    new TextBlockWidget($"  {xpInfo}"),
                    new TextBlockWidget($"  WEAPON: {op.EquippedWeaponName}"),
                    new TextBlockWidget($"  MODE: {op.CurrentMode}"),
                    new TextBlockWidget($"  PERKS: {op.UnlockedPerks.Count}"),
                    op.IsDead ? new TextBlockWidget("  STATUS: KIA") : new TextBlockWidget("")
                ]),
                new TextBlockWidget("  "),
                UI.CreateBorder("BASE CAMP", 38, 12, menuWidgets)
            ]),
            new TextBlockWidget(""),
            UI.CreateStatusBar($"Operator ID: {op.Id}")
        ]);
    }

    Hex1bWidget BuildStartMission()
    {
        return new VStackWidget([
            UI.CreateBorder("MISSION BRIEFING", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("INFILTRATION", 60, 18, [
                new TextBlockWidget("  OBJECTIVE: Engage hostile target"),
                new TextBlockWidget("  TIME LIMIT: 30 minutes"),
                new TextBlockWidget("  THREAT LEVEL: Variable"),
                new TextBlockWidget(""),
                new TextBlockWidget("  WARNING: Death is permanent."),
                new TextBlockWidget("           Health does not regenerate."),
                new TextBlockWidget(""),
                new TextBlockWidget("  Select action:"),
                new TextBlockWidget(""),
                new ButtonWidget("  ► BEGIN INFILTRATION").OnClick(_ => StartMission()),
                new ButtonWidget("  ► CANCEL").OnClick(_ => CurrentScreen = Screen.BaseCamp)
            ]),
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
                ErrorMessage = $"Failed to create combat session: {sessionResponse.StatusCode}";
                Message = $"Combat session creation failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }
            
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
        
        var playerHpBar = UI.CreateBar("HP", (int)player.Health, (int)player.MaxHealth, 20);
        var enemyHpBar = UI.CreateBar("HP", (int)enemy.Health, (int)enemy.MaxHealth, 20);
        
        return new VStackWidget([
            UI.CreateBorder("COMBAT SESSION", 78, 5),
            new TextBlockWidget(""),
            new HStackWidget([
                UI.CreateBorder("PLAYER", 38, 10, [
                    new TextBlockWidget($"  {player.Name}"),
                    new TextBlockWidget($"  {playerHpBar}"),
                    new TextBlockWidget($"  AMMO: {player.CurrentAmmo}/{player.MagazineSize}"),
                    new TextBlockWidget($"  COVER: {player.CurrentCover}"),
                    new TextBlockWidget($"  MOVE: {player.CurrentMovement}")
                ]),
                new TextBlockWidget("  "),
                UI.CreateBorder("ENEMY", 38, 10, [
                    new TextBlockWidget($"  {enemy.Name} (LVL {session.EnemyLevel})"),
                    new TextBlockWidget($"  {enemyHpBar}"),
                    new TextBlockWidget($"  AMMO: {enemy.CurrentAmmo}/{enemy.MagazineSize}"),
                    new TextBlockWidget($"  DIST: {player.DistanceToOpponent:F1}m")
                ])
            ]),
            new TextBlockWidget(""),
            UI.CreateBorder("ACTIONS", 78, 10, [
                new TextBlockWidget($"  TURN: {session.TurnNumber}  PHASE: {session.Phase}  TIME: {session.CurrentTimeMs}ms"),
                new TextBlockWidget(""),
                new ButtonWidget("  ► SUBMIT INTENTS (Not Implemented)").OnClick(_ => {
                    Message = "Intent submission not yet implemented in Pokemon UI.\nUse API directly for now.\n\nPress OK to continue.";
                    CurrentScreen = Screen.Message;
                    ReturnScreen = Screen.CombatSession;
                }),
                new ButtonWidget("  ► ADVANCE TURN").OnClick(_ => AdvanceCombat()),
                new ButtonWidget("  ► VIEW DETAILS").OnClick(_ => {
                    var pet = session.Pet;
                    Message = $"Combat Details:\n\nPlayer: {session.Player.Name}\nHealth: {session.Player.Health:F0}/{session.Player.MaxHealth:F0}\nAmmo: {session.Player.CurrentAmmo}/{session.Player.MagazineSize}\n\nEnemy: {session.Enemy.Name}\nHealth: {session.Enemy.Health:F0}/{session.Enemy.MaxHealth:F0}\n\nPet Health: {pet.Health:F0}\nPet Morale: {pet.Morale:F0}\n\nPress OK to continue.";
                    CurrentScreen = Screen.Message;
                    ReturnScreen = Screen.CombatSession;
                }),
                new ButtonWidget("  ► RETURN TO BASE").OnClick(_ => {
                    CurrentScreen = Screen.BaseCamp;
                    RefreshOperator();
                })
            ]),
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
            UI.CreateBorder("MISSION COMPLETE", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("DEBRIEFING", 60, 15, [
                new TextBlockWidget(""),
                new TextBlockWidget(Message ?? "Mission ended."),
                new TextBlockWidget(""),
                new ButtonWidget("  ► RETURN TO BASE").OnClick(_ => CurrentScreen = Screen.BaseCamp)
            ]),
            UI.CreateStatusBar("Mission complete")
        ]);
    }

    Hex1bWidget BuildMessage()
    {
        var lines = Message?.Split('\n') ?? [""];
        var widgets = new List<Hex1bWidget>();
        
        foreach (var line in lines)
        {
            widgets.Add(new TextBlockWidget($"  {line}"));
        }
        
        widgets.Add(new TextBlockWidget(""));
        widgets.Add(new ButtonWidget("  ► OK").OnClick(_ => CurrentScreen = ReturnScreen));

        return new VStackWidget([
            UI.CreateBorder("MESSAGE", 78, 5),
            new TextBlockWidget(""),
            UI.CreateBorder("INFO", 70, Math.Max(lines.Length + 6, 12), widgets),
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
    public static Hex1bWidget CreateBorder(string title, int width, int height, List<Hex1bWidget>? content = null)
    {
        // Use hex1b's BorderWidget for proper border rendering
        var borderContent = content != null && content.Count > 0 
            ? new VStackWidget(content)
            : CreateEmptyContent(height);

        // Try BorderWidget with just content and title
        // If it doesn't support title in constructor, we'll just use the border
        return new BorderWidget(borderContent);
    }

    private static Hex1bWidget CreateEmptyContent(int height)
    {
        var emptyContent = new List<Hex1bWidget>();
        for (int i = 0; i < Math.Max(1, height - 2); i++)
        {
            emptyContent.Add(new TextBlockWidget(""));
        }
        return new VStackWidget(emptyContent);
    }

    public static Hex1bWidget CreateStatusBar(string text)
    {
        return new TextBlockWidget($"  {text}");
    }

    public static string CreateBar(string label, int current, int max, int barWidth)
    {
        var pct = max > 0 ? (float)current / max : 0;
        var filled = (int)(pct * barWidth);
        var bar = new string('█', Math.Max(0, filled)) + new string('░', Math.Max(0, barWidth - filled));
        return $"{label}: {bar} {current}/{max}";
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
