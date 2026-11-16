@echo off
rem Runs Guardrail Server in this window (close it to stop). Data goes in the "data" folder next to this file.
cd /d "%~dp0"
start "" http://localhost:5080
GuardrailServer.exe
