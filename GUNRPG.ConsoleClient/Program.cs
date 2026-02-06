using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Core.Intents;

var baseAddress = Environment.GetEnvironmentVariable("GUNRPG_API_BASE") ?? "http://localhost:5209";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
jsonOptions.Converters.Add(new JsonStringEnumConverter());

using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };

Guid? activeSessionId = null;
Console.WriteLine($"GUNRPG Console Client");
Console.WriteLine($"API base: {httpClient.BaseAddress}");
Console.WriteLine();

while (true)
{
    ShowMenu(activeSessionId);
    var key = Console.ReadKey(true);
    Console.WriteLine();

    switch (key.KeyChar)
    {
        case '1':
            activeSessionId = await CreateSessionAsync(httpClient, jsonOptions);
            break;
        case '2':
            activeSessionId = await ShowStateAsync(httpClient, jsonOptions, activeSessionId);
            break;
        case '3':
            activeSessionId = await SubmitIntentsAsync(httpClient, jsonOptions, activeSessionId);
            break;
        case '4':
            activeSessionId = await AdvanceAsync(httpClient, jsonOptions, activeSessionId);
            break;
        case '5':
            activeSessionId = await ApplyPetActionAsync(httpClient, jsonOptions, activeSessionId);
            break;
        case '0':
            Console.WriteLine("Goodbye.");
            return;
        default:
            Console.WriteLine("Unknown option. Try again.");
            break;
    }

    Console.WriteLine();
}

static void ShowMenu(Guid? sessionId)
{
    Console.WriteLine("═══ GUNRPG API CLIENT ═══");
    Console.WriteLine($"Active session: {(sessionId.HasValue ? sessionId : "none")}");
    Console.WriteLine("1. Start new session");
    Console.WriteLine("2. View session state");
    Console.WriteLine("3. Submit intents (fire/move/stance/cover)");
    Console.WriteLine("4. Advance execution");
    Console.WriteLine("5. Apply pet action (rest/eat/drink/mission)");
    Console.WriteLine("0. Exit");
    Console.Write("Choose an option: ");
}

static async Task<Guid?> CreateSessionAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions)
{
    Console.Write("Enter player name (or press Enter for default): ");
    var playerName = Console.ReadLine();

    var request = new SessionCreateRequest
    {
        PlayerName = string.IsNullOrWhiteSpace(playerName) ? null : playerName
    };

    try
    {
        var response = await httpClient.PostAsJsonAsync("sessions", request, jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to create session: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        var session = await response.Content.ReadFromJsonAsync<CombatSessionDto>(jsonOptions);
        if (session == null)
        {
            Console.WriteLine("No session payload received.");
            return null;
        }

        Console.WriteLine($"Session created: {session.Id}");
        PrintState(session);
        return session.Id;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating session: {ex.Message}");
        return null;
    }
}

static async Task<Guid?> ShowStateAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions, Guid? sessionId)
{
    var id = await EnsureSession(httpClient, jsonOptions, sessionId);
    if (id == null)
    {
        return sessionId;
    }

    try
    {
        var state = await httpClient.GetFromJsonAsync<CombatSessionDto>($"sessions/{id}/state", jsonOptions);
        if (state == null)
        {
            Console.WriteLine("Session not found.");
            return sessionId;
        }

        PrintState(state);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error fetching state: {ex.Message}");
    }

    return id;
}

static async Task<Guid?> SubmitIntentsAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions, Guid? sessionId)
{
    var id = await EnsureSession(httpClient, jsonOptions, sessionId);
    if (id == null)
    {
        return sessionId;
    }

    var intent = PromptForIntents();
    var request = new SubmitIntentsRequest { Intents = intent };

    try
    {
        var response = await httpClient.PostAsJsonAsync($"sessions/{id}/intent", request, jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Intent rejected: {(int)response.StatusCode} {response.ReasonPhrase}");
            var error = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.WriteLine(error);
            }
            return id;
        }

        var result = await response.Content.ReadFromJsonAsync<IntentSubmissionResultDto>(jsonOptions);
        if (result?.State != null)
        {
            Console.WriteLine(result.Accepted ? "Intents accepted." : "Intents rejected.");
            PrintState(result.State);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error submitting intents: {ex.Message}");
    }

    return id;
}

static async Task<Guid?> AdvanceAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions, Guid? sessionId)
{
    var id = await EnsureSession(httpClient, jsonOptions, sessionId);
    if (id == null)
    {
        return sessionId;
    }

    try
    {
        var response = await httpClient.PostAsync($"sessions/{id}/advance", content: null);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Advance failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return id;
        }

        var state = await response.Content.ReadFromJsonAsync<CombatSessionDto>(jsonOptions);
        if (state != null)
        {
            Console.WriteLine("Advanced session.");
            PrintState(state);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error advancing session: {ex.Message}");
    }

    return id;
}

static async Task<Guid?> ApplyPetActionAsync(HttpClient httpClient, JsonSerializerOptions jsonOptions, Guid? sessionId)
{
    var id = await EnsureSession(httpClient, jsonOptions, sessionId);
    if (id == null)
    {
        return sessionId;
    }

    Console.Write("Choose pet action (rest/eat/drink/mission): ");
    var action = Console.ReadLine()?.Trim().ToLowerInvariant();

    var request = new PetActionRequest { Action = action ?? "rest" };
    switch (action)
    {
        case "eat":
            request.Nutrition = PromptFloat("Nutrition amount", 30f);
            break;
        case "drink":
            request.Hydration = PromptFloat("Hydration amount", 40f);
            break;
        case "mission":
            request.HitsTaken = (int)PromptFloat("Hits taken", 0f);
            request.OpponentDifficulty = PromptFloat("Opponent difficulty (10-100)", 50f);
            break;
        default:
            request.Hours = PromptFloat("Rest hours", 1f);
            break;
    }

    try
    {
        var response = await httpClient.PostAsJsonAsync($"sessions/{id}/pet", request, jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Pet action failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return id;
        }

        var petState = await response.Content.ReadFromJsonAsync<PetStateDto>(jsonOptions);
        if (petState != null)
        {
            Console.WriteLine("Pet state updated:");
            Console.WriteLine($"  Health:   {petState.Health:F0}");
            Console.WriteLine($"  Fatigue:  {petState.Fatigue:F0}");
            Console.WriteLine($"  Stress:   {petState.Stress:F0}");
            Console.WriteLine($"  Morale:   {petState.Morale:F0}");
            Console.WriteLine($"  Hunger:   {petState.Hunger:F0}");
            Console.WriteLine($"  Hydration:{petState.Hydration:F0}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error applying pet action: {ex.Message}");
    }

    return id;
}

static IntentDto PromptForIntents()
{
    Console.WriteLine("Primary action: 1=Fire, 2=Reload, 3=None");
    var primary = Console.ReadKey(true).KeyChar switch
    {
        '1' => PrimaryAction.Fire,
        '2' => PrimaryAction.Reload,
        _ => PrimaryAction.None
    };
    Console.WriteLine($"Selected {primary}");

    Console.WriteLine("Movement: 1=WalkToward, 2=WalkAway, 3=SprintToward, 4=SprintAway, 5=Stand");
    var movement = Console.ReadKey(true).KeyChar switch
    {
        '1' => MovementAction.WalkToward,
        '2' => MovementAction.WalkAway,
        '3' => MovementAction.SprintToward,
        '4' => MovementAction.SprintAway,
        _ => MovementAction.Stand
    };
    Console.WriteLine($"Selected {movement}");

    Console.WriteLine("Stance: 1=EnterADS, 2=ExitADS, 3=None");
    var stance = Console.ReadKey(true).KeyChar switch
    {
        '1' => StanceAction.EnterADS,
        '2' => StanceAction.ExitADS,
        _ => StanceAction.None
    };
    Console.WriteLine($"Selected {stance}");

    Console.WriteLine("Cover: 1=EnterPartial, 2=EnterFull, 3=Exit, 4=None");
    var cover = Console.ReadKey(true).KeyChar switch
    {
        '1' => CoverAction.EnterPartial,
        '2' => CoverAction.EnterFull,
        '3' => CoverAction.Exit,
        _ => CoverAction.None
    };
    Console.WriteLine($"Selected {cover}");

    Console.Write("Cancel current movement? (y/N): ");
    var cancelMovement = Console.ReadKey(true).KeyChar is 'y' or 'Y';
    Console.WriteLine(cancelMovement ? "Will cancel current movement." : "Keep movement if active.");

    return new IntentDto
    {
        Primary = primary,
        Movement = movement,
        Stance = stance,
        Cover = cover,
        CancelMovement = cancelMovement
    };
}

static void PrintState(CombatSessionDto state)
{
    Console.WriteLine($"Session {state.Id} | Phase: {state.Phase} | Turn: {state.TurnNumber} | Time: {state.CurrentTimeMs}ms");
    Console.WriteLine($"Player: {state.Player.Name} HP {state.Player.Health:F0}/{state.Player.MaxHealth:F0} Ammo {state.Player.CurrentAmmo}/{state.Player.MagazineSize}");
    Console.WriteLine($"Enemy : {state.Enemy.Name} HP {state.Enemy.Health:F0}/{state.Enemy.MaxHealth:F0} Ammo {state.Enemy.CurrentAmmo}/{state.Enemy.MagazineSize}");
    Console.WriteLine($"Distance: {state.Player.DistanceToOpponent:F1}m | Cover: {state.Player.CurrentCover} | Movement: {state.Player.CurrentMovement}");
    Console.WriteLine($"Pet: Health {state.Pet.Health:F0} Fatigue {state.Pet.Fatigue:F0} Stress {state.Pet.Stress:F0} Morale {state.Pet.Morale:F0} Hunger {state.Pet.Hunger:F0} Hydration {state.Pet.Hydration:F0}");
    Console.WriteLine($"XP: {state.PlayerXp:N0} | Level: {state.PlayerLevel} | Enemy Level: {state.EnemyLevel}");
}

static async Task<Guid?> EnsureSession(HttpClient httpClient, JsonSerializerOptions jsonOptions, Guid? current)
{
    if (current.HasValue)
    {
        return current;
    }

    Console.WriteLine("No session active. Creating one...");
    return await CreateSessionAsync(httpClient, jsonOptions);
}

static float PromptFloat(string label, float defaultValue)
{
    Console.Write($"{label} (default {defaultValue}): ");
    var input = Console.ReadLine();
    if (float.TryParse(input, out var value))
    {
        return value;
    }
    return defaultValue;
}
