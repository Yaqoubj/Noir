from PyInstaller.utils.hooks import copy_metadata

# Windows wheels expose webrtcvad, but publish metadata as webrtcvad-wheels.
datas = copy_metadata("webrtcvad-wheels")
hiddenimports = ["_webrtcvad"]
