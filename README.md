# Custom Paintings

Change purchased paintings to display custom images of your choice

## Install

### Mod managers

Install Custom Paintings through [Sailwind Mod Synchronizer](https://github.com/foxyv/SailwindModSynchronizer/releases/latest), r2modman or Thunderstore Mod Manager, then launch Sailwind through your manager. Dependencies are installed automatically.

### Manual installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) in your Sailwind game folder, following its installation instructions.
2. Download and extract the [Custom Paintings ZIP](https://github.com/Skeptic043/sailwind-custom-paintings/releases/latest). Copy its `BepInEx/plugins/CustomPaintings` folder into `BepInEx/plugins` in your Sailwind folder.
3. Launch Sailwind normally.

## How it works

- Add your picture folders to `Picture folders` in the settings. PNG and JPG pictures are supported
- Look at or hold an owned painting and press R to choose a picture from your selected folder(s). Nailed paintings don't show the highlight prompt but still allow changing their picture
- The `Original Art` button restores the painting back to its standard look
- Each painting keeps its respective picture stored across saves, and removing the mod returns the painting to normal safely

## Configuration

Settings can be found in `BepInEx/config/com.skeptic043.sailwind.custompaintings.cfg` after the first launch, or in game with [BepInEx Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager). Additional folders show the next time you open the picture selection interface. Size and compression changes apply immediately.

| Setting | Default | Options |
| --- | --- | --- |
| `Picture folders` | Blank | Folders to choose pictures from, separated by `\|`, for example `D:\Pictures \| E:\Ship Art`. |
| `Maximum size` | `2048` | Pictures larger than this size are shrunk to match. `1024`, `2048` or `4096`. |
| `Compress pictures` | `true` | Store pictures compressed in video memory, using about a sixth of the memory with a slight loss of quality. |

## Compatibility

No known incompatibilities.

## AI Use

AI was used to write all of the code in this project. The original design direction, testing, debugging, and release decisions are my own. If you prefer not to use mods developed with AI assistance, I understand and respect that choice.

## Links

[Report an issue](https://github.com/Skeptic043/sailwind-custom-paintings/issues) · [Source code](https://github.com/Skeptic043/sailwind-custom-paintings) · [Release notes](https://github.com/Skeptic043/sailwind-custom-paintings/blob/main/CHANGELOG.md) · [Build instructions](https://github.com/Skeptic043/sailwind-custom-paintings/blob/main/BUILDING.md) · [License](https://github.com/Skeptic043/sailwind-custom-paintings/blob/main/LICENSE) · [Support on Ko-fi](https://ko-fi.com/skeptic043) · skeptic043
