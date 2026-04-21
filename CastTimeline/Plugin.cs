using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using CastTimeline.Windows;
using CastTimeline.Services;

namespace CastTimeline;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private bool wasInCombat;
    private bool countdownWasActive;
    private bool isReplayActive;
    private DateTime pullTime;

    private const string CommandName = "/timeline";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("CastTimeline");
    public ConfigWindow ConfigWindow { get; init; }
    public MainWindow MainWindow { get; init; }
    public TimelineWindow TimelineWindow { get; init; }

    public FFLogsService FFLogsService { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize services
        FFLogsService = new FFLogsService(Log, PluginInterface);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        TimelineWindow = new TimelineWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(TimelineWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the CastTimeline manager window. Use 'show' to toggle the overlay or 'config' to open settings."
        });

        Framework.Update += OnFrameworkUpdate;

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [CastTimeline] ===A cool log message from Cast Timeline===
        Log.Information($"===CastTimeline plugin loaded===");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        TimelineWindow.Dispose();

        FFLogsService?.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    // Runs every game frame. Drives the replay state machine:
    //
    //  Idle ──(countdown starts)──► Replaying (negative fight time = countdown period)
    //       ──(combat starts, no countdown)──► Replaying (fight time starts at 0)
    //  Replaying ──(combat ends)──► Idle
    //
    // pullTime is the wall-clock moment that corresponds to fight time 0 (the pull).
    // During a countdown, pullTime = now + remainingSeconds, so fight time is negative
    // while the countdown is active and crosses zero exactly at the pull.
    // While the countdown is ticking we keep re-computing pullTime from the agent's
    // TimeRemaining each frame to correct for any accumulation drift.
    private void OnFrameworkUpdate(IFramework framework)
    {
        bool inCombat;
        var countdownActive = false;
        var countdownRemaining = 0f;
        unsafe
        {
            inCombat = Condition[ConditionFlag.InCombat];

            var agentModule = AgentModule.Instance();
            if (agentModule != null)
            {
                var agent = (AgentCountDownSettingDialog*)agentModule->GetAgentByInternalId(AgentId.CountDownSettingDialog);
                if (agent != null && agent->Active)
                {
                    countdownActive = true;
                    countdownRemaining = agent->TimeRemaining;
                }
            }
        }

        if (!TimelineWindow.IsOpen)
        {
            wasInCombat = inCombat;
            countdownWasActive = countdownActive;
            return;
        }

        if (countdownActive && !countdownWasActive)
        {
            // Countdown just started — derive pull time from the remaining seconds
            pullTime = DateTime.Now.AddSeconds(countdownRemaining);
            isReplayActive = true;
            TimelineWindow.StartReplay();
        }

        if (countdownActive)
        {
            // Re-sync every tick to correct drift
            pullTime = DateTime.Now.AddSeconds(countdownRemaining);
        }

        if (inCombat && !wasInCombat && !countdownWasActive && !isReplayActive)
        {
            // Combat started with no preceding countdown (e.g. open-world aggro)
            pullTime = DateTime.Now;
            isReplayActive = true;
            TimelineWindow.StartReplay();
        }

        if (!inCombat && wasInCombat)
        {
            // Wipe or kill — stop replaying
            isReplayActive = false;
            TimelineWindow.StopReplay();
        }

        if (isReplayActive)
            TimelineWindow.UpdateReplayTime((float)(DateTime.Now - pullTime).TotalMilliseconds);

        wasInCombat = inCombat;
        countdownWasActive = countdownActive;
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "show":   TimelineWindow.Toggle(); break;
            case "config": ConfigWindow.Toggle();   break;
            default:       MainWindow.Toggle();     break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleTimelineUi() => TimelineWindow.Toggle();
}
