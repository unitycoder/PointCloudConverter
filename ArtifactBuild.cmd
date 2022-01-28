@echo off
pushd "%~dp0"
powershell Compress-7Zip "Bin\Release" -ArchiveFileName "PointCloudConverterX64.zip" -Format Zip
:exit
popd
@echo on
