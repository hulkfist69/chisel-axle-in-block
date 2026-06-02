@echo off
REM Shim so you can type ".\log" instead of ".\pushlogs.ps1". Args pass through,
REM e.g.  .\log -Branch test-logs
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pushlogs.ps1" %*
