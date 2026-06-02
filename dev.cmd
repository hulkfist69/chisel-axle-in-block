@echo off
REM Shim so you can type ".\dev" instead of ".\dev.ps1". Args pass through,
REM e.g.  .\dev -PushLogs
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev.ps1" %*
