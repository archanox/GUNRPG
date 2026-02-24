using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Intents;
using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using Hex1b;
using Hex1b.Widgets;
using JsonSerializer = System.Text.Json.JsonSerializer;

var baseAddress = Environment.GetEnvironmentVariable("GUNRPG_API_BASE") ?? "http://localhost:5209";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.Converters.Add(new JsonStringEnumConverter());

using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
using var cts = new CancellationTokenSource();

// Initialize offline services via centralized factory (no manual new() for infrastructure types)
var offlineDbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".gunrpg", "offline.db");
var (offlineDb, offlineStore, backendResolver) = InfrastructureServiceExtensions.CreateConsoleServices(
    httpClient, offlineDbPath, jsonOptions);
using var _ = offlineDb; // ensure disposal

// Resolve game backend based on server reachability and local state
var backend = await backendResolver.ResolveAsync();

var gameState = new GameState(httpClient, jsonOptions, backend, backendResolver, offlineStore, offlineDb);

// Try to auto-load last used operator
gameState.LoadSavedOperatorId();
// LoadSavedOperatorId will set the appropriate screen (CombatSession or BaseCamp)

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var app = new Hex1bApp(ctx => gameState.BuildUI(ctx, cts));
await app.RunAsync(cts.Token);

class GameState(HttpClient client, JsonSerializerOptions options, IGameBackend backend, GameBackendResolver backendResolver, OfflineStore? offlineStore = null, LiteDB.LiteDatabase? offlineDb = null)
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
    
    // Intent selection state
    public string SelectedPrimary { get; set; } = "None";
    public string SelectedMovement { get; set; } = "Stand";
    public string SelectedStance { get; set; } = "None";
    public string SelectedCover { get; set; } = "None";
    // Disk-persisted combat service for offline play (uses same LiteDB file as operator snapshots)
    private CombatSessionService? _localCombatService;
    private bool _usingLocalCombat;
    private int _activeOfflineMissionSeed;
    private readonly IDeterministicCombatEngine _deterministicEngine = new DeterministicCombatEngine();

    public Task<Hex1bWidget> BuildUI(RootContext ctx, CancellationTokenSource cts)
    {
        return CurrentScreen switch
        {
            Screen.MainMenu => Task.FromResult<Hex1bWidget>(BuildMainMenu(cts)),
            Screen.SelectOperator => Task.FromResult<Hex1bWidget>(BuildSelectOperator()),
            Screen.CreateOperator => Task.FromResult<Hex1bWidget>(BuildCreateOperator()),
            Screen.BaseCamp => Task.FromResult<Hex1bWidget>(BuildBaseCamp()),
            Screen.StartMission => Task.FromResult<Hex1bWidget>(BuildStartMission()),
            Screen.CombatSession => Task.FromResult<Hex1bWidget>(BuildCombatSession(ctx)),
            Screen.MissionComplete => Task.FromResult<Hex1bWidget>(BuildMissionComplete()),
            Screen.Message => Task.FromResult<Hex1bWidget>(BuildMessage()),
            Screen.ChangeLoadout => Task.FromResult<Hex1bWidget>(BuildChangeLoadout()),
            Screen.TreatWounds => Task.FromResult<Hex1bWidget>(BuildTreatWounds()),
            Screen.UnlockPerk => Task.FromResult<Hex1bWidget>(BuildUnlockPerk()),
            Screen.AbortMission => Task.FromResult<Hex1bWidget>(BuildAbortMission()),
            Screen.PetActions => Task.FromResult<Hex1bWidget>(BuildPetActions()),
            _ => Task.FromResult<Hex1bWidget>(new TextBlockWidget("Unknown screen"))
        };
    }

    Hex1bWidget BuildMainMenu(CancellationTokenSource cts)
    {
        var mode = backendResolver.CurrentMode;
        var modeLabel = mode switch
        {
            GameMode.Online => "[ONLINE]",
            GameMode.Offline => "[OFFLINE]",
            GameMode.Blocked => "[OFFLINE - NO OPERATOR]",
            _ => "[UNKNOWN]"
        };

        var menuItems = new List<string>();

        if (mode != GameMode.Offline && mode != GameMode.Blocked)
        {
            menuItems.Add("CREATE NEW OPERATOR");
        }

        menuItems.Add("SELECT OPERATOR");

        menuItems.Add("EXIT");

        return new VStackWidget([
            UI.CreateBorder($"GUNRPG - OPERATOR TERMINAL {modeLabel}"),
            new TextBlockWidget(""),
            mode == GameMode.Blocked
                ? new TextBlockWidget("  ⚠ Server unreachable and no infiled operator. Gameplay blocked.")
                : new TextBlockWidget(""),
            UI.CreateBorder("MAIN MENU", new VStackWidget([
                new TextBlockWidget("  Select an option:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems.ToArray()).OnItemActivated(e => {
                    var selected = menuItems[e.ActivatedIndex];
                    switch (selected)
                    {
                        case "CREATE NEW OPERATOR":
                            if (mode == GameMode.Offline || mode == GameMode.Blocked)
                            {
                                ErrorMessage = "Cannot create operators while offline.";
                                Message = "Operator creation requires a server connection.\n\nPress OK to continue.";
                                CurrentScreen = Screen.Message;
                                ReturnScreen = Screen.MainMenu;
                            }
                            else
                            {
                                CurrentScreen = Screen.CreateOperator;
                                OperatorName = "";
                            }
                            break;
                        case "SELECT OPERATOR":
                            if (mode == GameMode.Blocked)
                            {
                                ErrorMessage = "Cannot select operators while server is unreachable.";
                                Message = "Server connection required to load operator list.\n\nPress OK to continue.";
                                CurrentScreen = Screen.Message;
                                ReturnScreen = Screen.MainMenu;
                            }
                            else
                            {
                                ErrorMessage = null;
                                LoadOperatorList();
                                CurrentScreen = Screen.SelectOperator;
                            }
                            break;
                        case "EXIT":
                            cts.Cancel();
                            break;
                    }
                })
            ])),
            new TextBlockWidget(""),
            UI.CreateStatusBar($"API: {client.BaseAddress} | Mode: {modeLabel}"),
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
            // LoadOperator may set screen to CombatSession if auto-resuming, so only set BaseCamp if not already set
            if (CurrentScreen != Screen.CombatSession)
            {
                CurrentScreen = Screen.BaseCamp;
            }
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
        // First, cleanup any completed sessions to prevent stuck state
        try
        {
            using var cleanupResponse = client.PostAsync($"operators/{operatorId}/cleanup", null).GetAwaiter().GetResult();
            // Cleanup failures are non-fatal; the Get operation will handle dangling references
        }
        catch
        {
            // Silently ignore cleanup errors; Get will still work
        }

        using var response = client.GetAsync($"operators/{operatorId}").GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to load operator: {response.StatusCode}");
        }

        var operatorDto = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
        CurrentOperator = ParseOperator(operatorDto);
        
        // Auto-resume combat if operator has active session and is still in Infil mode
        if (CurrentOperator?.CurrentMode == "Infil" &&
            operatorDto.TryGetProperty("activeCombatSession", out var sessionJson) && 
            sessionJson.ValueKind != JsonValueKind.Null)
        {
            CurrentSession = ParseSession(sessionJson, options);
            ActiveSessionId = CurrentSession?.Id;
            CurrentScreen = Screen.CombatSession;
        }
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
                    // Use IGameBackend — works for both online (HTTP) and offline (LiteDB snapshot)
                    var dto = backend.GetOperatorAsync(operatorId.ToString()).GetAwaiter().GetResult();
                    if (dto != null)
                    {
                        CurrentOperator = OperatorStateFromDto(dto);
                        // If operator is in Infil mode with an active combat session, resume it —
                        // but only when online, as LoadSession() requires HTTP access.
                        if (dto.ActiveCombatSessionId.HasValue && dto.CurrentMode == "Infil"
                            && backend is OnlineGameBackend)
                        {
                            ActiveSessionId = dto.ActiveCombatSessionId;
                            LoadSession();
                        }
                        if (CurrentScreen != Screen.CombatSession)
                        {
                            CurrentScreen = Screen.BaseCamp;
                        }
                    }
                    else
                    {
                        // No snapshot found (offline) or operator not found (online); clear stale ID
                        CurrentOperatorId = null;
                        SaveCurrentOperatorId();
                    }
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
        // Guard: operator creation is not allowed offline
        if (backendResolver.CurrentMode == GameMode.Offline || backendResolver.CurrentMode == GameMode.Blocked)
        {
            ErrorMessage = "Cannot create operators while offline. Server connection required.";
            return;
        }

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
        const string BaseActionInfil = "INFIL";
        const string InfilActionEngageCombat = "ENGAGE COMBAT";
        const string InfilActionExfil = "EXFIL";

        var op = CurrentOperator;
        if (op == null)
        {
            return new TextBlockWidget("No operator loaded");
        }

        var menuItems = new List<string>();
        var hasActiveCombatSession = op.ActiveCombatSessionId.HasValue || ActiveSessionId.HasValue;

        if (op.CurrentMode == "Base")
        {
            menuItems.Add(BaseActionInfil);
            menuItems.Add("CHANGE LOADOUT");
            menuItems.Add("TREAT WOUNDS");
            menuItems.Add("UNLOCK PERK");
            menuItems.Add("PET ACTIONS");
            menuItems.Add("VIEW STATS");
        }
        else // Infil mode
        {
            // In Infil mode, always allow engaging in combat
            // After a victory, ActiveSessionId is cleared but operator stays in Infil mode
            // In this case, we need to create a new session for the next combat
            menuItems.Add(InfilActionEngageCombat);
            menuItems.Add(InfilActionExfil);
            menuItems.Add("VIEW STATS");
        }

        menuItems.Add("MAIN MENU");

        var menuWidget = new ListWidget(menuItems.ToArray()).OnItemActivated(e => {
            var selectedItem = menuItems[e.ActivatedIndex];
            if (op.CurrentMode == "Base")
            {
                switch (selectedItem)
                {
                    case BaseActionInfil:
                        CurrentScreen = Screen.StartMission;
                        break;
                    case "CHANGE LOADOUT":
                        CurrentScreen = Screen.ChangeLoadout;
                        break;
                    case "TREAT WOUNDS":
                        CurrentScreen = Screen.TreatWounds;
                        break;
                    case "UNLOCK PERK":
                        CurrentScreen = Screen.UnlockPerk;
                        break;
                    case "PET ACTIONS":
                        CurrentScreen = Screen.PetActions;
                        break;
                    case "VIEW STATS":
                        Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nPerks: {string.Join(", ", op.UnlockedPerks)}\n\nPress OK to continue.";
                        CurrentScreen = Screen.Message;
                        ReturnScreen = Screen.BaseCamp;
                        break;
                    case "MAIN MENU":
                        CurrentScreen = Screen.MainMenu;
                        break;
                }
            }
            else // Infil mode
            {
                switch (selectedItem)
                {
                    case InfilActionEngageCombat:
                        // If we have an active combat session, load it
                        // If not, start a new combat session using the infil session
                        if (hasActiveCombatSession)
                        {
                            ActiveSessionId ??= op.ActiveCombatSessionId;
                            LoadSession();
                        }
                        else if (backend is not OnlineGameBackend)
                        {
                            // Offline: run combat using a local in-memory session (no server required)
                            if (StartOfflineCombatSession())
                            {
                                CurrentScreen = Screen.CombatSession;
                            }
                            else
                            {
                                Message = $"Failed to start offline combat.\n\n{ErrorMessage ?? "Unknown error"}\n\nPress OK to continue.";
                                CurrentScreen = Screen.Message;
                                ReturnScreen = Screen.BaseCamp;
                            }
                        }
                        else
                        {
                            // After a victory, ActiveCombatSessionId is cleared but operator stays in Infil
                            // Call the new endpoint to start a fresh combat session
                            if (StartNewCombatSession())
                            {
                                CurrentScreen = Screen.CombatSession;
                            }
                            else
                            {
                                Message = "Failed to start next combat.\n\nPress OK to continue.";
                                CurrentScreen = Screen.Message;
                                ReturnScreen = Screen.BaseCamp;
                            }
                        }
                        break;
                    case InfilActionExfil:
                        CurrentScreen = Screen.AbortMission;
                        break;
                    case "VIEW STATS":
                        Message = $"Operator: {op.Name}\nXP: {op.TotalXp}\nHealth: {op.CurrentHealth:F0}/{op.MaxHealth:F0}\nWeapon: {op.EquippedWeaponName}\nMission In Progress\n\nPress OK to continue.";
                        CurrentScreen = Screen.Message;
                        ReturnScreen = Screen.BaseCamp;
                        break;
                    case "MAIN MENU":
                        CurrentScreen = Screen.MainMenu;
                        break;
                }
            }
        });

        var healthBar = UI.CreateProgressBar("HP", (int)op.CurrentHealth, (int)op.MaxHealth, 30);
        var xpInfo = $"XP: {op.TotalXp}  EXFIL STREAK: {op.ExfilStreak}";

        // Create mode-specific title
        var modeTitle = op.CurrentMode == "Base" ? "BASE CAMP" : "FIELD OPS (INFIL)";
        var modeDescription = op.CurrentMode == "Base"
            ? "  Ready for new missions and maintenance"
            : "  Mission in progress - limited actions available";

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
                UI.CreateBorder(modeTitle, new VStackWidget([
                    new TextBlockWidget(modeDescription),
                    new TextBlockWidget(""),
                    menuWidget
                ]))
            ]),
            new TextBlockWidget(""),
            UI.CreateStatusBar($"Operator ID: {op.Id}")
        ]);
    }

    Hex1bWidget BuildStartMission()
    {
        var menuItems = new[] {
            "BEGIN INFIL",
            "CANCEL"
        };

        return new VStackWidget([
            UI.CreateBorder("COMBAT BRIEFING"),
            new TextBlockWidget(""),
            UI.CreateBorder("INFIL", new VStackWidget([
                new TextBlockWidget("  OBJECTIVE: Engage hostile target"),
                new TextBlockWidget("  TIME LIMIT: 30 minutes"),
                new TextBlockWidget("  THREAT LEVEL: Variable"),
                new TextBlockWidget(""),
                new TextBlockWidget("  WARNING: On death, your streak resets and you are returned to base."),
                new TextBlockWidget(""),
                new TextBlockWidget("  Select action:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // BEGIN INFIL
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
            // Start the infil - this only transitions the operator to Infil mode
            // Combat sessions are created when the player chooses to engage in combat
            var response = client.PostAsync($"operators/{CurrentOperatorId}/infil/start", null).GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to infil: {response.StatusCode} - {errorContent}";
                Message = $"Infil failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            var result = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
            // Don't set ActiveSessionId - it will be set when player engages in combat
            CurrentOperator = ParseOperator(result.GetProperty("operator"));

            // Save offline snapshot so the operator is available if the server becomes unreachable.
            // This is the single infil path: one server call + one local persist.
            // NOTE: GetAwaiter().GetResult() is required here — hex1b event handlers are synchronous.
            if (backend is OnlineGameBackend onlineBackend)
            {
                try
                {
                    onlineBackend.InfilOperatorAsync(CurrentOperatorId!.Value.ToString()).GetAwaiter().GetResult();
                }
                catch (Exception snapshotEx)
                {
                    // Snapshot save failure is non-fatal — offline play simply won't be available
                    Console.WriteLine($"[INFIL] Snapshot save failed (non-fatal): {snapshotEx.Message}");
                }
            }

            // Return to Field Ops; player can choose to engage combat or exfil
            CurrentScreen = Screen.BaseCamp;
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
                
                // If session is completed, either continue infil with a fresh target or show completion screen.
                if (sessionData.Phase == "Completed")
                {
                    // In infil mode, completed combat can continue with a fresh target in the same infil session.
                    if (CurrentOperator?.CurrentMode == "Infil" && StartNextCombatInInfil())
                    {
                        CurrentScreen = Screen.CombatSession;
                    }
                    else
                    {
                        Message = string.IsNullOrWhiteSpace(ErrorMessage)
                            ? "Mission completed."
                            : $"Unable to start next combat session.\nError: {ErrorMessage}";
                        CurrentScreen = Screen.MissionComplete;
                    }
                }
                else
                {
                    CurrentScreen = Screen.CombatSession;
                }
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            // Session doesn't exist - this can happen if operator was created before session creation was implemented
            // Force end the infil by processing a failed outcome to reset the operator to Base mode
            ErrorMessage = "Combat session not found - forcing exfil";
            Message = $"Infil session not found in database.\n\nThis can happen with operators created before the latest updates.\nForcing exfil to reset operator state.\n\nPress OK to continue.";
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

    bool StartNextCombatInInfil()
    {
        ErrorMessage = null;

        if (!ActiveSessionId.HasValue || CurrentOperatorId == null || CurrentOperator == null)
        {
            return false;
        }

        try
        {
            // Delete the completed combat session
            using var deleteResponse = client.DeleteAsync($"sessions/{ActiveSessionId.Value}").GetAwaiter().GetResult();
            if (!deleteResponse.IsSuccessStatusCode)
            {
                return false;
            }

            // Call the /infil/combat endpoint to start a new combat session
            // This emits CombatSessionStartedEvent and returns the new session ID
            using var startCombatResponse = client.PostAsync($"operators/{CurrentOperatorId}/infil/combat", null).GetAwaiter().GetResult();
            if (!startCombatResponse.IsSuccessStatusCode)
            {
                var errorContent = startCombatResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to start combat session: {startCombatResponse.StatusCode} - {errorContent}";
                return false;
            }

            var newSessionId = startCombatResponse.Content.ReadFromJsonAsync<Guid>(options).GetAwaiter().GetResult();

            // Create the combat session with the new ID
            var sessionRequest = new
            {
                id = newSessionId,
                operatorId = CurrentOperatorId,
                playerName = CurrentOperator.Name,
                weaponName = CurrentOperator.EquippedWeaponName,
                playerLevel = 1,
                playerMaxHealth = CurrentOperator.MaxHealth,
                playerCurrentHealth = CurrentOperator.CurrentHealth
            };

            using var sessionResponse = client.PostAsJsonAsync("sessions", sessionRequest, options).GetAwaiter().GetResult();
            if (!sessionResponse.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to create combat session: {sessionResponse.StatusCode}";
                return false;
            }

            CurrentSession = sessionResponse.Content.ReadFromJsonAsync<CombatSessionDto>(options).GetAwaiter().GetResult();
            if (CurrentSession == null)
            {
                ErrorMessage = $"Failed to deserialize replacement combat session";
                return false;
            }

            // Update ActiveSessionId to point to the new session
            ActiveSessionId = newSessionId;

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Starts a new offline combat session backed by the disk-persisted LiteDB store.
    /// Used when the server is unreachable but the operator has an active infil snapshot.
    /// </summary>
    bool StartOfflineCombatSession()
    {
        ErrorMessage = null;

        if (CurrentOperator == null || CurrentOperatorId == null)
            return false;

        if (CurrentOperator.CurrentMode != "Infil")
        {
            ErrorMessage = "Cannot start combat: operator is not in Infil mode.";
            return false;
        }

        if (offlineDb == null)
        {
            ErrorMessage = "Offline database unavailable";
            return false;
        }

        try
        {
            // Lazy-initialize the local combat service (reused across combats within a session)
            if (_localCombatService == null)
            {
                var localStore = new LiteDbCombatSessionStore(offlineDb);
                _localCombatService = new CombatSessionService(localStore);
            }

            var request = new SessionCreateRequest
            {
                OperatorId = CurrentOperatorId,
                PlayerName = CurrentOperator.Name,
                Seed = RandomNumberGenerator.GetInt32(int.MaxValue)
            };
            _activeOfflineMissionSeed = request.Seed.Value;

            var result = _localCombatService.CreateSessionAsync(request).GetAwaiter().GetResult();
            if (!result.IsSuccess)
            {
                ErrorMessage = result.ErrorMessage;
                return false;
            }

            CurrentSession = ToLocalDto(result.Value!);
            ActiveSessionId = CurrentSession.Id;
            _usingLocalCombat = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    bool StartNewCombatSession()
    {
        ErrorMessage = null;

        if (CurrentOperatorId == null || CurrentOperator == null)
        {
            return false;
        }

        try
        {
            // Start a new combat session using the /infil/combat endpoint
            // This emits a CombatSessionStartedEvent and returns the new session ID
            using var response = client.PostAsync($"operators/{CurrentOperatorId}/infil/combat", null).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to start combat session: {response.StatusCode} - {errorContent}";
                return false;
            }

            var newSessionId = response.Content.ReadFromJsonAsync<Guid>(options).GetAwaiter().GetResult();
            
            // Now create the combat session with the new ID
            var sessionRequest = new
            {
                id = newSessionId,
                operatorId = CurrentOperatorId,
                playerName = CurrentOperator.Name,
                weaponName = CurrentOperator.EquippedWeaponName,
                playerLevel = 1,
                playerMaxHealth = CurrentOperator.MaxHealth,
                playerCurrentHealth = CurrentOperator.CurrentHealth
            };

            using var sessionResponse = client.PostAsJsonAsync("sessions", sessionRequest, options).GetAwaiter().GetResult();
            if (!sessionResponse.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to create combat session: {sessionResponse.StatusCode}";
                return false;
            }

            CurrentSession = sessionResponse.Content.ReadFromJsonAsync<CombatSessionDto>(options).GetAwaiter().GetResult();
            if (CurrentSession == null)
            {
                ErrorMessage = $"Failed to deserialize combat session";
                return false;
            }

            // Update ActiveSessionId to point to the new session
            ActiveSessionId = newSessionId;

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    Hex1bWidget BuildCombatSession(RootContext ctx)
    {
        var session = CurrentSession;
        if (session == null)
        {
            return new TextBlockWidget("No session loaded");
        }

        var player = session.Player;
        var enemy = session.Enemy;

        // Check if combat has ended
        var combatEnded = player.Health <= 0 || enemy.Health <= 0 || session.Phase == "Completed";

        // Create progress bars for HP
        var playerHpBar = UI.CreateProgressBar("HP", (int)player.Health, (int)player.MaxHealth, 20);
        var enemyHpBar = UI.CreateProgressBar("HP", (int)enemy.Health, (int)enemy.MaxHealth, 20);

        // Create progress bars for stamina
        var playerStaminaBar = UI.CreateProgressBar("STA", (int)player.Stamina, 100, 15);

        // Create ADS progress indicator (showing aim state)
        var adsStatus = player.AimState switch
        {
            "ADS" => "[ADS]",
            "Hip" or "HIP" => "[HIP]",
            "TransitioningToADS" or "TransitioningToHip" => "[TRANS]",
            _ => $"[{player.AimState}]"
        };

        // Create cover visual representation
        var coverVisual = UI.CreateCoverVisual(player.CurrentCover);

        // Create battle log display (Pokemon-style)
        var battleLogWidget = UI.CreateBattleLogDisplay(session.BattleLog);

        // Build action menu based on combat state
        var actionItems = new List<string>();
        if (!combatEnded)
        {
            actionItems.Add("SUBMIT INTENTS");
        }
        actionItems.Add("VIEW DETAILS");
        actionItems.Add("RETREAT");

        var combatContentWidget = new VStackWidget([
            UI.CreateBorder("⚔ COMBAT MISSION ⚔"),
            new TextBlockWidget(""),
            new HStackWidget([
                // Player column
                UI.CreateBorder("🎮 PLAYER", new VStackWidget([
                    new TextBlockWidget($"  {player.Name}"),
                    new TextBlockWidget("  "),
                    playerHpBar,
                    playerStaminaBar,
                    new TextBlockWidget($"  AMMO: {player.CurrentAmmo}/{player.MagazineSize} {adsStatus}"),
                    new TextBlockWidget("  "),
                    new TextBlockWidget($"  {coverVisual}"),
                    new TextBlockWidget($"  MOVE: {player.MovementState}")
                ])),
                new TextBlockWidget("    "),
                // Enemy column
                UI.CreateBorder("💀 ENEMY", new VStackWidget([
                    new TextBlockWidget($"  {enemy.Name} (LVL {session.EnemyLevel})"),
                    new TextBlockWidget("  "),
                    enemyHpBar,
                    new TextBlockWidget($"  AMMO: {enemy.CurrentAmmo}/{enemy.MagazineSize}"),
                    new TextBlockWidget($"  DIST: {player.DistanceToOpponent:F2}m"),
                    new TextBlockWidget($"  COVER: {enemy.CurrentCover}")
                ]))
            ]),
            new TextBlockWidget(""),
            // Battle log display - Pokemon Red style
            battleLogWidget,
            new TextBlockWidget(""),
            UI.CreateBorder("ACTIONS", new VStackWidget([
                new TextBlockWidget($"  TURN: {session.TurnNumber}  PHASE: {session.Phase}  TIME: {session.CurrentTimeMs}ms"),
                new TextBlockWidget(""),
                new ListWidget(actionItems.ToArray()).OnItemActivated(e => {
                    var selectedAction = actionItems[e.ActivatedIndex];
                    switch (selectedAction)
                    {
                        case "SUBMIT INTENTS":
                            // Reset intent selections to defaults then open the floating window
                            SelectedPrimary = "None";
                            SelectedMovement = "Stand";
                            SelectedStance = "None";
                            SelectedCover = "None";
                            OpenSubmitIntentsWindow(e.Windows, session);
                            break;
                        case "VIEW DETAILS":
                            var pet = session.Pet;
                            Message = $"Combat Details:\n\nPlayer: {session.Player.Name}\nHealth: {session.Player.Health:F0}/{session.Player.MaxHealth:F0}\nAmmo: {session.Player.CurrentAmmo}/{session.Player.MagazineSize}\n\nEnemy: {session.Enemy.Name}\nHealth: {session.Enemy.Health:F0}/{session.Enemy.MaxHealth:F0}\n\nPet Health: {pet.Health:F0}\nPet Morale: {pet.Morale:F0}\n\nPress OK to continue.";
                            CurrentScreen = Screen.Message;
                            ReturnScreen = Screen.CombatSession;
                            break;
                        case "RETREAT":
                            // Process combat outcome if ended
                            if (combatEnded)
                            {
                                if (_usingLocalCombat)
                                    ProcessCombatOutcomeOffline();
                                else
                                    ProcessCombatOutcome();
                            }
                            // Retreat: delete the combat session and return to Infil mode
                            if (ActiveSessionId.HasValue)
                            {
                                if (_usingLocalCombat)
                                {
                                    try
                                    {
                                        _localCombatService!.DeleteSessionAsync(ActiveSessionId.Value).GetAwaiter().GetResult();
                                    }
                                    catch
                                    {
                                        // Non-fatal — session will be orphaned in offline.db but won't affect gameplay
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        // Delete the session server-side so it won't auto-resume
                                        var deleteResponse = client.DeleteAsync($"sessions/{ActiveSessionId}").GetAwaiter().GetResult();
                                        deleteResponse.Dispose();
                                    }
                                    catch
                                    {
                                        // If delete fails, still clear locally - better than nothing
                                    }
                                }
                            }
                            _usingLocalCombat = false;
                            ActiveSessionId = null;
                            CurrentSession = null;
                            RefreshOperator();
                            Message = "Retreated from combat.\nYou remain in infil mode.\n\nPress OK to continue.";
                            CurrentScreen = Screen.Message;
                            ReturnScreen = Screen.BaseCamp;
                            break;
                    }
                })
            ])),
            UI.CreateStatusBar($"Session: {session.Id}")
        ]);

        return ctx.WindowPanel().Background(_ => combatContentWidget).Fill();
    }

    void OpenSubmitIntentsWindow(WindowManager windows, CombatSessionDto session)
    {
        var primaryActions = new[] { "None", "Fire", "Reload" };
        var movementActions = new[] { "Stand", "WalkToward", "WalkAway", "SprintToward", "SprintAway", "SlideToward", "SlideAway", "Crouch" };
        var stanceActions = new[] { "None", "EnterADS", "ExitADS" };
        var coverActions = new[] { "None", "EnterPartial", "EnterFull", "Exit" };

        var player = session.Player;

        windows.Window(w => new VStackWidget([
            new TabPanelWidget([
                new TabItemWidget("🎯 PRIMARY", _ => [
                    new TextBlockWidget($"  Current: {SelectedPrimary}"),
                    new TextBlockWidget(""),
                    new ListWidget(primaryActions).OnItemActivated(e => { SelectedPrimary = e.ActivatedText; })
                ]),
                new TabItemWidget("🏃 MOVEMENT", _ => [
                    new TextBlockWidget($"  Current: {SelectedMovement}"),
                    new TextBlockWidget(""),
                    new ListWidget(movementActions).OnItemActivated(e => { SelectedMovement = e.ActivatedText; })
                ]),
                new TabItemWidget("🧍 STANCE", _ => [
                    new TextBlockWidget($"  Current: {SelectedStance}"),
                    new TextBlockWidget(""),
                    new ListWidget(stanceActions).OnItemActivated(e => { SelectedStance = e.ActivatedText; })
                ]),
                new TabItemWidget("🏠 COVER", _ => [
                    new TextBlockWidget($"  Current: {SelectedCover}"),
                    new TextBlockWidget(""),
                    new ListWidget(coverActions).OnItemActivated(e => { SelectedCover = e.ActivatedText; })
                ]),
            ]),
            new TextBlockWidget(""),
            UI.CreateBorder("SELECTIONS", new HStackWidget([
                new TextBlockWidget($"  PRIMARY: {SelectedPrimary}"),
                new TextBlockWidget("   MOVEMENT: "),
                new TextBlockWidget(SelectedMovement),
                new TextBlockWidget("   STANCE: "),
                new TextBlockWidget(SelectedStance),
                new TextBlockWidget("   COVER: "),
                new TextBlockWidget(SelectedCover),
            ])),
            new TextBlockWidget(""),
            new TextBlockWidget($"  {player.Name}: HP {player.Health:F0}/{player.MaxHealth:F0}  AMMO: {player.CurrentAmmo}/{player.MagazineSize}  Stamina: {player.Stamina:F0}"),
            new TextBlockWidget(""),
            new HStackWidget([
                new TextBlockWidget("  "),
                new ButtonWidget("SUBMIT & ADVANCE TURN").OnClick(_ => {
                    // Only advance the turn if intent submission succeeded
                    if (SubmitPlayerIntents())
                    {
                        // NOTE: AdvanceCombat blocks on HTTP calls due to hex1b's synchronous event handlers.
                        AdvanceCombat();
                        w.Window.Cancel();
                    }
                }),
                new TextBlockWidget("  "),
                new ButtonWidget("CANCEL").OnClick(_ => w.Window.Cancel()),
            ])
        ]))
        .Title("⚡ SUBMIT INTENTS ⚡")
        .Size(72, 22)
        .Open(windows);
    }

    void AdvanceCombat()
    {
        if (_usingLocalCombat)
        {
            AdvanceCombatOffline();
            return;
        }

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
                    
                    // Clear local session state to prevent auto-resume after death
                    ActiveSessionId = null;
                    CurrentSession = null;
                    
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

    void AdvanceCombatOffline()
    {
        try
        {
            var result = _localCombatService!.AdvanceAsync(ActiveSessionId!.Value).GetAwaiter().GetResult();
            if (!result.IsSuccess)
            {
                ErrorMessage = $"Advance failed: {result.ErrorMessage}";
                return;
            }

            CurrentSession = ToLocalDto(result.Value!);

            if (CurrentSession.Player.Health <= 0 || CurrentSession.Enemy.Health <= 0)
            {
                // Combat ended — update offline snapshot with outcome, then clear local combat flag
                ProcessCombatOutcomeOffline();
                _usingLocalCombat = false;

                // Delete the completed local session to keep offline.db clean
                try
                {
                    _localCombatService!.DeleteSessionAsync(ActiveSessionId!.Value).GetAwaiter().GetResult();
                }
                catch
                {
                    // Non-fatal — orphaned session won't affect gameplay
                }

                // Keep CurrentSession for display on MissionComplete screen
                ActiveSessionId = null;

                Message = CurrentSession.Player.Health <= 0
                    ? "MISSION FAILED\n\nYou were eliminated.\n\nPress OK to continue."
                    : "MISSION SUCCESS\n\nTarget eliminated.\n\nPress OK to continue.";
                CurrentScreen = Screen.MissionComplete;
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
            // Use authoritative session ID with fallback
            var sessionId = ActiveSessionId ?? CurrentOperator?.ActiveCombatSessionId;
            
            // Guard against null sessionId - API requires non-null Guid
            if (!sessionId.HasValue)
            {
                ErrorMessage = "No active session found to process outcome";
                return;
            }
            
            // NOTE: Using empty request body - server will load session and compute outcome
            var request = new { SessionId = sessionId.Value };
            using var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/infil/outcome", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                // Check if this is an InvalidState error (operator already in Base mode)
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (errorContent.Contains("InvalidState") || errorContent.Contains("already in Base mode"))
                {
                    // Silently ignore - operator is already in the correct state
                    return;
                }
                
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

    /// <summary>
    /// Processes a combat outcome for an offline session using the deterministic combat engine.
    /// The engine computes the authoritative result from the initial operator state and seed,
    /// ensuring the server can independently replay and verify the outcome.
    /// </summary>
    void ProcessCombatOutcomeOffline()
    {
        if (CurrentOperator == null || offlineStore == null)
            return;

        var initialDto = ToBackendDto(CurrentOperator);
        var initialHash = OfflineMissionHashing.ComputeOperatorStateHash(initialDto);

        // Run the deterministic combat engine — same engine the server will use to verify.
        var combatResult = _deterministicEngine.Execute(initialDto, _activeOfflineMissionSeed);
        var updatedDto = combatResult.ResultOperator;

        var resultHash = OfflineMissionHashing.ComputeOperatorStateHash(updatedDto);
        var nextSequence = offlineStore.GetNextMissionSequence(updatedDto.Id);

        var initialSnapshotJson = JsonSerializer.Serialize(initialDto, options);
        var resultSnapshotJson = JsonSerializer.Serialize(updatedDto, options);

        Console.WriteLine($"[OFFLINE] Envelope seq={nextSequence} seed={_activeOfflineMissionSeed} initialHash={initialHash} resultHash={resultHash}");

        var envelope = new OfflineMissionEnvelope
        {
            OperatorId = updatedDto.Id,
            SequenceNumber = nextSequence,
            RandomSeed = _activeOfflineMissionSeed,
            InitialSnapshotJson = initialSnapshotJson,
            ResultSnapshotJson = resultSnapshotJson,
            InitialOperatorStateHash = initialHash,
            ResultOperatorStateHash = resultHash,
            FullBattleLog = combatResult.BattleLog,
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        };

        offlineStore.SaveMissionResult(envelope);
        offlineStore.UpdateOperatorSnapshot(updatedDto.Id, updatedDto);
        CurrentOperator = OperatorStateFromDto(updatedDto);
    }

    private static OperatorDto ToBackendDto(OperatorState state)
    {
        return new OperatorDto
        {
            Id = state.Id.ToString(),
            Name = state.Name,
            TotalXp = state.TotalXp,
            CurrentHealth = state.CurrentHealth,
            MaxHealth = state.MaxHealth,
            EquippedWeaponName = state.EquippedWeaponName,
            UnlockedPerks = state.UnlockedPerks,
            ExfilStreak = state.ExfilStreak,
            IsDead = state.IsDead,
            CurrentMode = state.CurrentMode,
            ActiveCombatSessionId = state.ActiveCombatSessionId,
            InfilSessionId = state.InfilSessionId,
            InfilStartTime = state.InfilStartTime,
            LockedLoadout = state.LockedLoadout,
            Pet = state.Pet == null ? null : new GUNRPG.Application.Dtos.PetStateDto
            {
                Health = state.Pet.Health,
                Fatigue = state.Pet.Fatigue,
                Injury = state.Pet.Injury,
                Stress = state.Pet.Stress,
                Morale = state.Pet.Morale,
                Hunger = state.Pet.Hunger,
                Hydration = state.Pet.Hydration,
                LastUpdated = state.Pet.LastUpdated
            }
        };
    }

    void RefreshOperator()
    {
        if (backend is not OnlineGameBackend)
        {
            // Offline: re-read from local snapshot without any HTTP calls
            try
            {
                if (CurrentOperatorId.HasValue)
                {
                    var dto = backend.GetOperatorAsync(CurrentOperatorId.Value.ToString()).GetAwaiter().GetResult();
                    if (dto != null)
                        CurrentOperator = OperatorStateFromDto(dto);
                }
            }
            catch
            {
                // Silently fail - operator state will be stale but UI remains functional
            }
            return;
        }

        try
        {
            // First, cleanup any completed sessions to prevent stuck state
            try
            {
                using var cleanupResponse = client.PostAsync($"operators/{CurrentOperatorId}/cleanup", null).GetAwaiter().GetResult();
                // Cleanup failures are non-fatal; the Get operation will handle dangling references
            }
            catch
            {
                // Silently ignore cleanup errors; Get will still work
            }

            using var response = client.GetAsync($"operators/{CurrentOperatorId}").GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var operatorDto = response.Content.ReadFromJsonAsync<JsonElement>(options).GetAwaiter().GetResult();
                CurrentOperator = ParseOperator(operatorDto);
                
                // Auto-resume combat if operator has active session, is still in Infil mode, and we're not already in combat
                if (CurrentScreen != Screen.CombatSession && 
                    CurrentOperator?.CurrentMode == "Infil" &&
                    operatorDto.TryGetProperty("activeCombatSession", out var sessionJson) && 
                    sessionJson.ValueKind != JsonValueKind.Null)
                {
                    CurrentSession = ParseSession(sessionJson, options);
                    ActiveSessionId = CurrentSession?.Id;
                    CurrentScreen = Screen.CombatSession;
                }
            }
        }
        catch (Exception)
        {
            // Silently fail - operator state will be stale but UI remains functional
        }
    }


    bool SubmitPlayerIntents()
    {
        if (_usingLocalCombat)
        {
            return SubmitPlayerIntentsOffline();
        }

        try
        {
            var request = new
            {
                intents = new
                {
                    primary = SelectedPrimary,
                    movement = SelectedMovement,
                    stance = SelectedStance,
                    cover = SelectedCover,
                    cancelMovement = false
                }
            };

            using var response = client.PostAsJsonAsync($"sessions/{ActiveSessionId}/intent", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Intent submission failed: {response.StatusCode} - {errorContent}";
                Message = $"Intent submission failed:\n\n{errorContent}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.CombatSession;
                return false;
            }

            // Reload session state
            var sessionData = response.Content.ReadFromJsonAsync<CombatSessionDto>(options).GetAwaiter().GetResult();
            if (sessionData != null)
            {
                CurrentSession = sessionData;
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error submitting intents:\n\n{ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.CombatSession;
            return false;
        }
    }

    bool SubmitPlayerIntentsOffline()
    {
        try
        {
            var intents = new GUNRPG.Application.Dtos.IntentDto
            {
                Primary = Enum.TryParse<PrimaryAction>(SelectedPrimary, out var p) ? p : PrimaryAction.None,
                Movement = Enum.TryParse<MovementAction>(SelectedMovement, out var m) ? m : MovementAction.Stand,
                Stance = Enum.TryParse<StanceAction>(SelectedStance, out var st) ? st : StanceAction.None,
                Cover = Enum.TryParse<CoverAction>(SelectedCover, out var c) ? c : CoverAction.None,
                CancelMovement = false
            };

            var result = _localCombatService!.SubmitPlayerIntentsAsync(
                ActiveSessionId!.Value,
                new SubmitIntentsRequest { Intents = intents })
                .GetAwaiter().GetResult();

            if (!result.IsSuccess)
            {
                ErrorMessage = $"Intent submission failed: {result.ErrorMessage}";
                Message = $"Intent submission failed:\n\n{result.ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.CombatSession;
                return false;
            }

            CurrentSession = ToLocalDto(result.Value!);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error submitting intents:\n\n{ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.CombatSession;
            return false;
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
                new ListWidget(new[] { "OK" }).OnItemActivated(_ => {
                    ActiveSessionId = null;
                    CurrentSession = null;
                    RefreshOperator();
                    CurrentScreen = Screen.BaseCamp;
                })
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
        contentWidgets.Add(new ListWidget(new[] { "OK" }).OnItemActivated(_ => {
            // Refresh operator state before returning to BaseCamp to ensure menu shows correct mode
            if (ReturnScreen == Screen.BaseCamp)
            {
                RefreshOperator();
            }
            CurrentScreen = ReturnScreen;
        }));

        return new VStackWidget([
            UI.CreateBorder("MESSAGE"),
            new TextBlockWidget(""),
            UI.CreateBorder("INFO", new VStackWidget(contentWidgets)),
            UI.CreateStatusBar("Press OK to continue")
        ]);
    }

    Hex1bWidget BuildChangeLoadout()
    {
        var availableWeapons = new[] {
            "SOKOL 545",
            "STURMWOLF 45",
            "M15 MOD 0",
            "--- CANCEL ---"
        };

        return new VStackWidget([
            UI.CreateBorder("CHANGE LOADOUT"),
            new TextBlockWidget(""),
            UI.CreateBorder("AVAILABLE WEAPONS", new VStackWidget([
                new TextBlockWidget("  Select a weapon to equip:"),
                new TextBlockWidget(""),
                new TextBlockWidget($"  Current: {CurrentOperator?.EquippedWeaponName ?? "None"}"),
                new TextBlockWidget(""),
                new ListWidget(availableWeapons).OnItemActivated(e => {
                    if (e.ActivatedIndex == availableWeapons.Length - 1)
                    {
                        // Cancel
                        CurrentScreen = Screen.BaseCamp;
                    }
                    else
                    {
                        ChangeLoadout(availableWeapons[e.ActivatedIndex]);
                    }
                })
            ])),
            UI.CreateStatusBar("Choose a weapon")
        ]);
    }

    void ChangeLoadout(string weaponName)
    {
        try
        {
            var request = new { WeaponName = weaponName };
            var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/loadout", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to change loadout: {response.StatusCode} - {errorContent}";
                Message = $"Loadout change failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            // Refresh operator state
            RefreshOperator();
            
            Message = $"Loadout changed successfully.\nNew weapon: {weaponName}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error changing loadout: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    Hex1bWidget BuildTreatWounds()
    {
        var op = CurrentOperator;
        if (op == null)
        {
            return new TextBlockWidget("No operator loaded");
        }

        var maxHeal = op.MaxHealth - op.CurrentHealth;
        var healOptions = new List<(string display, float amount)>();
        
        if (maxHeal > 0)
        {
            if (maxHeal >= 25) healOptions.Add(("HEAL 25 HP", 25f));
            if (maxHeal >= 50) healOptions.Add(("HEAL 50 HP", 50f));
            if (maxHeal >= 100) healOptions.Add(("HEAL 100 HP", 100f));
            healOptions.Add(($"HEAL ALL ({maxHeal:F0} HP)", maxHeal));
        }
        else
        {
            healOptions.Add(("ALREADY AT FULL HEALTH", 0f));
        }
        
        healOptions.Add(("--- CANCEL ---", 0f));
        var displayOptions = healOptions.Select(h => h.display).ToArray();

        return new VStackWidget([
            UI.CreateBorder("TREAT WOUNDS"),
            new TextBlockWidget(""),
            UI.CreateBorder("MEDICAL", new VStackWidget([
                new TextBlockWidget($"  Current Health: {op.CurrentHealth:F0}/{op.MaxHealth:F0}"),
                new TextBlockWidget(""),
                new TextBlockWidget("  Select healing amount:"),
                new TextBlockWidget(""),
                new ListWidget(displayOptions).OnItemActivated(e => {
                    if (e.ActivatedIndex == healOptions.Count - 1)
                    {
                        // Cancel
                        CurrentScreen = Screen.BaseCamp;
                    }
                    else if (healOptions[e.ActivatedIndex].amount > 0)
                    {
                        TreatWounds(healOptions[e.ActivatedIndex].amount);
                    }
                    else
                    {
                        // Already at full health
                        CurrentScreen = Screen.BaseCamp;
                    }
                })
            ])),
            UI.CreateStatusBar("Choose healing amount")
        ]);
    }

    void TreatWounds(float healthAmount)
    {
        try
        {
            var request = new { HealthAmount = healthAmount };
            var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/wounds/treat", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to treat wounds: {response.StatusCode} - {errorContent}";
                Message = $"Wound treatment failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            // Refresh operator state
            RefreshOperator();
            
            Message = $"Wounds treated successfully.\nHealed: {healthAmount:F0} HP\nNew Health: {CurrentOperator?.CurrentHealth:F0}/{CurrentOperator?.MaxHealth:F0}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error treating wounds: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    Hex1bWidget BuildUnlockPerk()
    {
        var availablePerks = new[] {
            "Iron Lungs",
            "Quick Draw",
            "Toughness",
            "Fast Reload",
            "Steady Aim",
            "--- CANCEL ---"
        };

        var op = CurrentOperator;
        var unlockedPerks = op?.UnlockedPerks ?? new List<string>();

        return new VStackWidget([
            UI.CreateBorder("UNLOCK PERK"),
            new TextBlockWidget(""),
            UI.CreateBorder("AVAILABLE PERKS", new VStackWidget([
                new TextBlockWidget("  Select a perk to unlock:"),
                new TextBlockWidget(""),
                new TextBlockWidget($"  Unlocked: {string.Join(", ", unlockedPerks)}"),
                new TextBlockWidget(""),
                new ListWidget(availablePerks).OnItemActivated(e => {
                    if (e.ActivatedIndex == availablePerks.Length - 1)
                    {
                        // Cancel
                        CurrentScreen = Screen.BaseCamp;
                    }
                    else
                    {
                        UnlockPerk(availablePerks[e.ActivatedIndex]);
                    }
                })
            ])),
            UI.CreateStatusBar("Choose a perk")
        ]);
    }

    void UnlockPerk(string perkName)
    {
        try
        {
            var request = new { PerkName = perkName };
            var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/perks", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to unlock perk: {response.StatusCode} - {errorContent}";
                Message = $"Perk unlock failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            // Refresh operator state
            RefreshOperator();
            
            Message = $"Perk unlocked successfully.\nNew perk: {perkName}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error unlocking perk: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    Hex1bWidget BuildAbortMission()
    {
        var menuItems = new[] {
            "CONFIRM EXFIL",
            "CANCEL"
        };

        return new VStackWidget([
            UI.CreateBorder("EXFIL BACK TO BASE"),
            new TextBlockWidget(""),
            UI.CreateBorder("WARNING", new VStackWidget([
                new TextBlockWidget("  Are you sure you want to exfil?"),
                new TextBlockWidget(""),
                new TextBlockWidget("  - Mission outcome will be processed"),
                new TextBlockWidget("  - EXFIL streak and XP will reflect that outcome"),
                new TextBlockWidget(""),
                new TextBlockWidget("  Select action:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // CONFIRM EXFIL
                            ProcessExfil();
                            break;
                        case 1: // CANCEL
                            CurrentScreen = Screen.BaseCamp;
                            break;
                    }
                })
            ])),
            UI.CreateStatusBar("Confirm exfil")
        ]);
    }

    void ProcessExfil()
    {
        try
        {
            // Get combat session ID (may be null after victory or if no combat started)
            var activeCombatSessionId = ActiveSessionId ?? CurrentOperator?.ActiveCombatSessionId;
            
            // Get infil session ID (should always exist when in Infil mode)
            var infilSessionId = CurrentOperator?.InfilSessionId;
            
            // If no active combat session, use the new complete infil endpoint
            // This happens after victory (ActiveCombatSessionId cleared) or when exfiling without combat
            if (!activeCombatSessionId.HasValue)
            {
                // Validate we have an infil session - this should always be true when in Infil mode
                if (!infilSessionId.HasValue)
                {
                    // This is an error state - operator claims to be in Infil mode but has no infil session
                    Message = "Error: Invalid infil state.\nNo infil session found.\n\nPress OK to continue.";
                    CurrentScreen = Screen.Message;
                    ReturnScreen = Screen.BaseCamp;
                    return;
                }
                
                // Complete infil successfully using the new endpoint
                using var completeResponse = client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"operators/{CurrentOperatorId}/infil/complete"))
                    .GetAwaiter().GetResult();
                
                if (completeResponse.IsSuccessStatusCode)
                {
                    // Clear session state and refresh
                    ActiveSessionId = null;
                    CurrentSession = null;
                    RefreshOperator();
                    Message = "Exfil successful!\nReturning to base.\n\nPress OK to continue.";
                    CurrentScreen = Screen.Message;
                    ReturnScreen = Screen.BaseCamp;
                    return;
                }
                
                // If complete failed, show error
                var errorContent = completeResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to complete exfil: {completeResponse.StatusCode} - {errorContent}";
                Message = $"Exfil failed.\n{ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }
            
            // Legacy path: Process EXFIL for the completed combat session
            var request = new { SessionId = activeCombatSessionId.Value };
            using (var sessionStateResponse = client.GetAsync($"sessions/{activeCombatSessionId.Value}/state").GetAwaiter().GetResult())
            {
                if (!sessionStateResponse.IsSuccessStatusCode)
                {
                    RefreshOperator();
                    CurrentScreen = Screen.BaseCamp;
                    return;
                }

                var sessionState = sessionStateResponse.Content.ReadFromJsonAsync<CombatSessionDto>(options).GetAwaiter().GetResult();
                if (sessionState?.Phase != "Completed")
                {
                    RefreshOperator();
                    CurrentScreen = Screen.BaseCamp;
                    return;
                }
            }

            using var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/infil/outcome", request, options)
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                
                // If operator is already in Base mode, treat as success
                if (errorContent.Contains("InvalidState") || errorContent.Contains("not in Infil mode"))
                {
                    ActiveSessionId = null;
                    CurrentSession = null;
                    RefreshOperator();
                    Message = "Infil already ended.\nReturning to base.\n\nPress OK to continue.";
                    CurrentScreen = Screen.Message;
                    ReturnScreen = Screen.BaseCamp;
                    return;
                }

                ErrorMessage = $"Failed to exfil: {response.StatusCode} - {errorContent}";
                Message = $"Exfil failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.BaseCamp;
                return;
            }

            // Clear session
            ActiveSessionId = null;
            CurrentSession = null;

            // Refresh operator state
            RefreshOperator();

            Message = "Exfil processed.\nReturning to base.\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error processing exfil: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.BaseCamp;
        }
    }

    Hex1bWidget BuildPetActions()
    {
        var op = CurrentOperator;
        if (op?.Pet == null)
        {
            return new VStackWidget([
                UI.CreateBorder("PET ACTIONS"),
                new TextBlockWidget(""),
                new TextBlockWidget("  No pet data available."),
                new TextBlockWidget(""),
                new ListWidget(new[] { "BACK" }).OnItemActivated(_ => CurrentScreen = Screen.BaseCamp),
                UI.CreateStatusBar("No pet available")
            ]);
        }

        var pet = op.Pet;
        
        // Create pet stat progress bars (using 100 as max for percentages)
        var healthBar = UI.CreateProgressBar("Health", (int)pet.Health, 100, 20);
        var fatigueBar = UI.CreateProgressBar("Fatigue", (int)pet.Fatigue, 100, 20);
        var injuryBar = UI.CreateProgressBar("Injury", (int)pet.Injury, 100, 20);
        var stressBar = UI.CreateProgressBar("Stress", (int)pet.Stress, 100, 20);
        var moraleBar = UI.CreateProgressBar("Morale", (int)pet.Morale, 100, 20);
        var hungerBar = UI.CreateProgressBar("Hunger", (int)pet.Hunger, 100, 20);
        var hydrationBar = UI.CreateProgressBar("Hydration", (int)pet.Hydration, 100, 20);

        var menuItems = new[] {
            "REST (Reduce Fatigue)",
            "EAT (Reduce Hunger)",
            "DRINK (Increase Hydration)",
            "BACK"
        };

        return new VStackWidget([
            UI.CreateBorder("PET ACTIONS"),
            new TextBlockWidget(""),
            UI.CreateBorder("PET STATUS", new VStackWidget([
                new TextBlockWidget("  "),
                healthBar,
                fatigueBar,
                injuryBar,
                stressBar,
                moraleBar,
                hungerBar,
                hydrationBar,
                new TextBlockWidget("  ")
            ])),
            new TextBlockWidget(""),
            UI.CreateBorder("ACTIONS", new VStackWidget([
                new TextBlockWidget("  Select an action:"),
                new TextBlockWidget(""),
                new ListWidget(menuItems).OnItemActivated(e => {
                    switch (e.ActivatedIndex)
                    {
                        case 0: // REST
                            ApplyPetAction("rest", hours: 8);
                            break;
                        case 1: // EAT
                            ApplyPetAction("eat", nutrition: 50);
                            break;
                        case 2: // DRINK
                            ApplyPetAction("drink", hydration: 50);
                            break;
                        case 3: // BACK
                            CurrentScreen = Screen.BaseCamp;
                            break;
                    }
                })
            ])),
            UI.CreateStatusBar("Choose a pet action")
        ]);
    }

    void ApplyPetAction(string action, float? hours = null, float? nutrition = null, float? hydration = null)
    {
        try
        {
            var request = new
            {
                Action = action,
                Hours = hours,
                Nutrition = nutrition,
                Hydration = hydration
            };

            using var response = client.PostAsJsonAsync($"operators/{CurrentOperatorId}/pet", request, options)
                .GetAwaiter().GetResult();
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ErrorMessage = $"Failed to apply pet action: {response.StatusCode} - {errorContent}";
                Message = $"Pet action failed.\nError: {ErrorMessage}\n\nPress OK to continue.";
                CurrentScreen = Screen.Message;
                ReturnScreen = Screen.PetActions;
                return;
            }

            // Refresh operator state
            RefreshOperator();
            
            var actionText = action.ToUpperInvariant();
            Message = $"Pet action completed.\nAction: {actionText}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.PetActions;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Message = $"Error applying pet action: {ex.Message}\n\nPress OK to continue.";
            CurrentScreen = Screen.Message;
            ReturnScreen = Screen.PetActions;
        }
    }

    static OperatorState OperatorStateFromDto(OperatorDto dto)
    {
        PetState? pet = null;
        if (dto.Pet != null)
        {
            pet = new PetState
            {
                Health = dto.Pet.Health,
                Fatigue = dto.Pet.Fatigue,
                Injury = dto.Pet.Injury,
                Stress = dto.Pet.Stress,
                Morale = dto.Pet.Morale,
                Hunger = dto.Pet.Hunger,
                Hydration = dto.Pet.Hydration,
                LastUpdated = dto.Pet.LastUpdated
            };
        }

        return new OperatorState
        {
            Id = Guid.Parse(dto.Id),
            Name = dto.Name,
            TotalXp = dto.TotalXp,
            CurrentHealth = dto.CurrentHealth,
            MaxHealth = dto.MaxHealth,
            EquippedWeaponName = dto.EquippedWeaponName,
            UnlockedPerks = dto.UnlockedPerks,
            ExfilStreak = dto.ExfilStreak,
            IsDead = dto.IsDead,
            CurrentMode = dto.CurrentMode,
            ActiveCombatSessionId = dto.ActiveCombatSessionId,
            InfilSessionId = dto.InfilSessionId,
            InfilStartTime = dto.InfilStartTime,
            LockedLoadout = dto.LockedLoadout,
            Pet = pet
        };
    }

    static OperatorState ParseOperator(JsonElement json)
    {
        PetState? pet = null;
        if (json.TryGetProperty("pet", out var petJson) && petJson.ValueKind != JsonValueKind.Null)
        {
            pet = new PetState
            {
                Health = petJson.GetProperty("health").GetSingle(),
                Fatigue = petJson.GetProperty("fatigue").GetSingle(),
                Injury = petJson.GetProperty("injury").GetSingle(),
                Stress = petJson.GetProperty("stress").GetSingle(),
                Morale = petJson.GetProperty("morale").GetSingle(),
                Hunger = petJson.GetProperty("hunger").GetSingle(),
                Hydration = petJson.GetProperty("hydration").GetSingle(),
                LastUpdated = petJson.GetProperty("lastUpdated").GetDateTimeOffset()
            };
        }

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
            InfilSessionId = json.TryGetProperty("infilSessionId", out var infil) && infil.ValueKind != JsonValueKind.Null 
                ? infil.GetGuid() 
                : null,
            ActiveCombatSessionId = json.TryGetProperty("activeCombatSessionId", out var combat) && combat.ValueKind != JsonValueKind.Null 
                ? combat.GetGuid() 
                : null,
            LockedLoadout = json.GetProperty("lockedLoadout").GetString() ?? "",
            Pet = pet
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

    static CombatSessionDto? ParseSession(JsonElement json, JsonSerializerOptions jsonOptions)
    {
        // Deserialize directly from JsonElement
        return JsonSerializer.Deserialize<CombatSessionDto>(json.GetRawText(), jsonOptions);
    }

    /// <summary>
    /// Converts an Application-layer <see cref="GUNRPG.Application.Dtos.CombatSessionDto"/> to the
    /// console client's local <see cref="CombatSessionDto"/> by serializing with enum-as-string
    /// converters and then deserializing into the string-typed local model.
    /// </summary>
    CombatSessionDto ToLocalDto(GUNRPG.Application.Dtos.CombatSessionDto appDto)
    {
        var json = JsonSerializer.Serialize(appDto, options);
        return JsonSerializer.Deserialize<CombatSessionDto>(json, options)!;
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
        
        // Use BorderWidget with Title() method (hex1b 0.83.0 API)
        return new BorderWidget(borderContent).Title(title);
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

    /// <summary>
    /// Creates a Pokemon Red-style battle log display widget.
    /// Shows the most recent battle events in a bordered dialog box.
    /// </summary>
    public static Hex1bWidget CreateBattleLogDisplay(List<BattleLogEntryDto>? battleLog)
    {
        if (battleLog == null || battleLog.Count == 0)
        {
            return CreateBorder("📋 BATTLE LOG", new VStackWidget([
                new TextBlockWidget("  No events yet. Press ADVANCE TURN to begin combat."),
                new TextBlockWidget("")
            ]));
        }

        // Take last 6 entries to fit in a reasonable display area
        var recentEntries = battleLog.TakeLast(6).ToList();
        
        var logWidgets = new List<Hex1bWidget>();
        foreach (var entry in recentEntries)
        {
            var actorPrefix = !string.IsNullOrEmpty(entry.ActorName) 
                ? $"{entry.ActorName} " 
                : "";
            
            // Format message in Pokemon style
            var message = $"  {actorPrefix}{entry.Message}";
            logWidgets.Add(new TextBlockWidget(message));
        }
        
        // Add empty line for spacing
        logWidgets.Add(new TextBlockWidget(""));

        return CreateBorder("📋 BATTLE LOG", new VStackWidget(logWidgets.ToArray()));
    }

    /// <summary>
    /// Creates an ASCII art visualization of the cover state.
    /// </summary>
    public static string CreateCoverVisual(string coverState)
    {
        return coverState?.ToUpper() switch
        {
            "NONE" => "COVER: [   EXPOSED   ]",
            "PARTIAL" => "COVER: [ ▄ PARTIAL ▄ ]",
            "FULL" => "COVER: [███  FULL  ███]",
            _ => $"COVER: {coverState}"
        };
    }
}

class CombatSessionDto
{
    public Guid Id { get; init; }
    public Guid OperatorId { get; init; }
    public string Phase { get; init; } = "";
    public long CurrentTimeMs { get; init; }
    public PlayerStateDto Player { get; init; } = default!;
    public PlayerStateDto Enemy { get; init; } = default!;
    public PetStateDto Pet { get; init; } = default!;
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
    public List<BattleLogEntryDto> BattleLog { get; init; } = new();
}

class PlayerStateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Stamina { get; init; }
    public float Fatigue { get; init; }
    public float SuppressionLevel { get; init; }
    public bool IsSuppressed { get; init; }
    public float DistanceToOpponent { get; init; }
    public int CurrentAmmo { get; init; }
    public int? MagazineSize { get; init; }
    public string AimState { get; init; } = "";
    public string MovementState { get; init; } = "";
    public string CurrentMovement { get; init; } = "";
    public string CurrentDirection { get; init; } = "";
    public string CurrentCover { get; init; } = "";
    public bool IsMoving { get; init; }
    public bool IsAlive { get; init; }
}

class PetStateDto
{
    public float Health { get; init; }
    public float Fatigue { get; init; }
    public float Injury { get; init; }
    public float Stress { get; init; }
    public float Morale { get; init; }
    public float Hunger { get; init; }
    public float Hydration { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
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
    public Guid? InfilSessionId { get; init; }
    public Guid? ActiveCombatSessionId { get; init; }
    public string LockedLoadout { get; init; } = "";
    public PetState? Pet { get; init; }
}

class PetState
{
    public float Health { get; init; }
    public float Fatigue { get; init; }
    public float Injury { get; init; }
    public float Stress { get; init; }
    public float Morale { get; init; }
    public float Hunger { get; init; }
    public float Hydration { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}

class BattleLogEntryDto
{
    public string EventType { get; init; } = "";
    public long TimeMs { get; init; }
    public string Message { get; init; } = "";
    public string? ActorName { get; init; }
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
    Message,
    ChangeLoadout,
    TreatWounds,
    UnlockPerk,
    AbortMission,
    PetActions
}
