@echo off
pushd "%~dp0"
powershell Compress-7Zip "Bin\x64\Release\net8.0-windows10.0.22621.0" -ArchiveFileName "PointCloudConverterX64.zip" -Format Zip
:exit
popd
@echo on
