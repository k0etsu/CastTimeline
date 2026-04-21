# CastTimeline

This plugin displays a scrolling cast timeline overlay, synchronized in real time with your countdown and combat.

Import a rotation from [FFLogs](https://www.fflogs.com/) or a CSV file, then watch the timeline scroll automatically as the fight progresses so you always know what to press next.

<img width="480" height="145" alt="v68xqqL9LD" src="https://github.com/user-attachments/assets/cc242b78-6cc4-4b1e-b37d-45d96fcaee81" />

---

## Installation

CastTimeline is not available in the official Dalamud plugin list. To install it you need to add the custom repository first.

1. Open **XIVLauncher** → **Dalamud Settings** → **Experimental**.
2. Under **Custom Plugin Repositories**, add the following URL and click the **+** button:
   ```
   https://raw.githubusercontent.com/k0etsu/DalamudPlugins/master/pluginmaster.json
   ```
3. Click **Save & Close**, then open the **Plugin Installer**.
4. Search for **CastTimeline** and install it.

---

## How it works

Once a timeline is loaded, a floating overlay window appears showing all casts laid out horizontally on a time axis. GCDs are rendered in the lower lane and oGCDs in the upper lane (at a smaller size). A cast trail extends to the right of each icon representing the cast duration.

When a **countdown starts** or **combat begins**, the timeline starts scrolling automatically. A vertical playhead sits at 25% of the window width; the timeline scrolls so that the cast at the current fight time is always aligned with the playhead. When combat ends the timeline stops and resets.

The overlay can be repositioned, resized, and locked in place through the settings window.

---

## Importing a timeline

Open the **Fight Manager** window with `/timeline` and use one of the two import methods.

### FFLogs

1. Click **Import from FFLogs**.
2. Paste an FFLogs report URL (e.g. `https://www.fflogs.com/reports/ABC123`).
3. Click **Fetch Fights**, select the pull, then select the player.
4. Click **Import Cast Events**.

FFLogs credentials (OAuth2 client ID and secret) are required. These can be created at [fflogs.com/api/clients](https://www.fflogs.com/api/clients/) and saved in the **Import/Export** settings tab.

### CSV

1. Click **Import CSV**.
2. Type a file path or click **Browse...** to navigate to the file.
3. Enter the player name that will appear in tooltips.
4. Click **Import**.

---

## CSV format

The CSV file must have a header row followed by one action per line.

```
time,action,isGCD,castTime
```

| Column | Type | Description |
|--------|------|-------------|
| `time` | float | Seconds from pull. Negative values indicate a prepull cast (e.g. `-3.3` means the cast started 3.3 s before the countdown hit zero). |
| `action` | string or int | Ability name or numeric game ability ID. Use `Tincture` for a potion. Names that match the game's action sheet exactly will resolve to an icon automatically. |
| `isGCD` | int | `1` = GCD (spell / weaponskill), `0` = oGCD (ability / item). |
| `castTime` | float | Cast bar length in seconds. Use `0` for instant actions. |

### Example

```csv
time,action,isGCD,castTime
-3.32,Fire III,1,3.328
0,Tincture,0,0
0.7,Fire IV,1,2.168
2.87,Fire IV,1,2.168
5.04,Triplecast,0,0
5.74,Fire IV,1,2.168
7.91,Fire IV,1,2.168
10.08,Xenoglossy,1,0
```

### Generating a CSV from XIVInTheShell

[XIV in the Shell](https://xivintheshell.com/) can export rotation timelines in the exact format CastTimeline expects.

1. Build your rotation on the website.
2. Click **Export** → **Export as CSV**. (Use the option for Tischel's Plugin)
3. Import the downloaded file using the CSV import flow described above.

---

## Settings

Open the **Settings** window via the **Settings** button in the Fight Manager or `/timeline config`.

### Timeline Window

| Setting | Description |
|---------|-------------|
| Lock Timeline Window | Prevents the overlay from being moved or resized. |
| Background Alpha / Color | Controls the overlay background transparency and color. |
| Show Outline When Unlocked | Draws a border around the window when it is not locked. |
| Scale | Horizontal zoom of the timeline (how many pixels per second). |
| Icon Scale | Size of the ability icons. |
| Show Ruler | Toggles the time ruler along the top of the timeline. |
| Ruler Interval | Spacing between ruler tick marks in seconds. |
| Show Icon Labels | Toggles the small `0` / `1` ability type badge on each icon. |
| Use Custom Trail Color | Overrides the per-job trail color with a single custom color. |
| Remember Window Position | Saves the overlay position and size across sessions. |

### Replay controls

The **Reset Replay** button in the Fight Manager stops the active replay and jumps the timeline back to the beginning. It is only enabled while a replay is running, and is intentionally placed in the manager window (not the overlay) to avoid accidental presses during a fight.

---

## Commands

| Command | Action |
|---------|--------|
| `/timeline` | Toggle the Fight Manager window. |
| `/timeline show` | Toggle the timeline overlay. |
| `/timeline config` | Open the Settings window. |
