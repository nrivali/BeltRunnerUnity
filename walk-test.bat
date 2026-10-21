@echo off
rem The walk test: on the pad, out onto the hangar deck, down the ramp to the workshop terminal and back aboard; shots walk_*.png
start "" "%~dp0Builds\Windows\BeltRunner.exe" -walk -screen-fullscreen 0 -screen-width 1280 -screen-height 720
