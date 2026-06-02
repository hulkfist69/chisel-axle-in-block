@echo off
REM "f" dev dispatcher. Examples:  .\f dev   .\f test   .\f build   .\f log
REM (Add the profile function from the README to use bare "f dev" without .\)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0f.ps1" %*
