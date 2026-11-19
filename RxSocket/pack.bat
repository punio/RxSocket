@echo off
dotnet pack "%~dp0RxSocket.nuspec" --output "%~dp0nuget"
exit /b %errorlevel%
