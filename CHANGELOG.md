# Changelog

## 0.1.7

### Changed
- New users have their default picture quality set to 1024 pixels. This uses 25% of the memory that 2048-pixel pictures use, and the 2048-pixel option remains in the config
- Preparing a picture is spread over several frames to avoid a hitch
- Browsing in the picture menu for a long period of time no longer saves every thumbnail to memory
- Looking around no longer creates small amounts of garbage every frame

## Changed
- New installs default to 1024-pixel pictures, which use a quarter of the memory, and 2048 is still in the settings
- Preparing a picture is spread over several frames to avoid a hitch
- Pictures stay ready for a while after no painting shows them, so boats coming back into view do not reload them
- Long browsing in the picture menu no longer keeps every thumbnail
- Looking around no longer creates small amounts of garbage every frame

## 0.1.6
### Initial Release
- Allows interacting with paintings to replace the normal picture with a custom photo
