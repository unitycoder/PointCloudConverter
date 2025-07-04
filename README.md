# PointCloudConverter
This is pointCloud converter (commandline and GUI) for [Point Cloud Viewer &amp; Tools 3 (Unity plugin)](https://assetstore.unity.com/packages/tools/utilities/point-cloud-viewer-and-tools-3-310385?aid=1101lGti)

### Documentation
- Check [Wiki](https://github.com/unitycoder/PointCloudConverter/wiki)

### Download prebuild exe
- From Releases https://github.com/unitycoder/PointCloudConverter/releases

### Arguments
- https://github.com/unitycoder/PointCloudConverter/wiki/Commandline-Arguments

### Import Formats
- LAZ/LAS
- PLY (ascii/binary) *Initial version
- E57 *Experimental version
- (more to be added)

### Export Formats
- UCPC (V2) for https://github.com/unitycoder/UnityPointCloudViewer
- PCROOT (V3) for https://github.com/unitycoder/UnityPointCloudViewer
- GLTF (GLB) output https://las2gltf.kelobyte.fi/ *Paid plugin
- (more to be added)

### Requirements
- Windows 10 or later
- Visual Studio 2022 or later
- To view converted Point Clouds inside Unity, this viewer is required: from Unity Asset Store: https://github.com/unitycoder/UnityPointCloudViewer

### Pull Request
This standalone converter is open-source, so you can create your own Forks and versions.
Pull requests to improve this converter are welcome! (please create Issue first, so i/users can comment on it)

### Images
![image](https://github.com/user-attachments/assets/8b5c47cf-3532-4bc6-8b4e-1bfd3347d8a4)

### Building
- Open project in VS2019 or later
- Press F5 to build
- Executable is created in the /bin/ folder (you can launch it from command prompt, or from Explorer to use GUI)

### Notes
- See Project/PointCloudConverter Properties.. > Build Events / Post build: Small robocopy script is used to move output files into lib/ folder (so that executable is alone in the root folder)

### Powered by
[![JetBrains logo.](https://resources.jetbrains.com/storage/products/company/brand/logos/jetbrains.svg)](https://jb.gg/OpenSourceSupport)
