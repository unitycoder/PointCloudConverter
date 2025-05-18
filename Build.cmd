@echo off
pushd "%~dp0"
if exist Debug rd /s /q Debug
if exist Release rd /s /q Release
if exist x64 rd /s /q x64

"%programfiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\msbuild.exe" /p:Configuration=Release
REM "%programfiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\msbuild.exe" /p:Configuration=Release

:exit
popd
@echo on
