# Vega, the ship's onboard assistant: records the tutorial lines into Assets/Resources/Sfx/tut_<id>.mp3 with ElevenLabs.
# The lines must match Tutorial.cs word for word (the card shows the same text). The API key is read from the sibling
# BeltRunner repo's git-ignored elevenlabs.key and never written anywhere. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File tools/gen-voice.ps1            (skips lines whose mp3 exists)
#   powershell -ExecutionPolicy Bypass -File tools/gen-voice.ps1 -Force     (re-records everything)
param([string[]]$Only=@(), [switch]$Force)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$key=(Get-Content (Join-Path (Split-Path -Parent $root) 'BeltRunner\elevenlabs.key') -Raw).Trim()
$out=Join-Path $root 'Assets\Resources\Sfx'
$VOICE='kIYbb5iUo0dJb8oRw5Mt'   # the voice of Vega (the user's pick): energetic, approachable, friendly
$lines=[ordered]@{
  launch = "Vega here, your ship's assistant. Press W on the pad and approach control taxis you out of the hangar; the ship is yours the moment it lets go."
  steer  = "The mouse steers. W and S work the throttle, A and D roll, X cuts the throttle. Open her up and give me a turn."
  hud    = "The band along the bottom: SHIP is your hull, shield, fuel and hold. FLIGHT is your speed and thrust, and the way back to the cargo ship. TARGET is whatever you are looking at or locked on, and the weapon in hand."
  radar  = "Press R to pulse the radar. Every rock it reaches is marked for a while. Ore shows as coloured veins and crystals; plain grey rock is barren, so do not waste the laser on it."
  lock   = "Find a copper rock (orange veins), put the mouse on it and press Q to lock it. The target panel shows its size and what is left in it."
  mine   = "Get within laser reach and hold the left mouse button (Space or L too). The dish under the nose cuts while you hold. When the rock breaks, fly through the glow and the ore comes aboard."
  inv    = "Copper in the hold. Press Tab for your inventory: four slots, one stack each. Deposit all moves it aboard the cargo ship once you are docked."
  return = "Follow the CARGO SHIP readout. Within 2,250 press E and approach control brings you in, or fly slowly into either hangar mouth yourself."
  hangar = "Your tank fills from the cargo ship's fuel supply and your hull mends from its repair parts, one part per hull point. Both run down, and both restock at the Hub."
  stow   = "Press E, or Deposit all, to move your copper into the cargo ship's storage: 50 slots, and it all warps with you. Your hold is for the trip out; the storage is for the haul."
  refit  = "The services panel lists your refits: laser, engine, tank, cargo, scanner, hull. A bigger hold and a stronger laser pay for themselves fastest."
  hub    = "Nothing sells out here. Press N for the nav map and warp to the Hub. Meridian Colony buys everything, and it is where the cargo ship refuels and restocks."
  depart = "Press Depart (or W on the pad) to launch. The belt is all yours out there: fill the hold and bring it home. Keep an eye on the fuel; the pad tops you up every time you dock."
  done   = "That is the loop: fill the hold, stow it, warp to the Hub, sell, refit, repeat. Vega out. Good hunting."
}
$chars=0
foreach ($id in $lines.Keys){
  if ($Only.Count -gt 0 -and $Only -notcontains $id) { continue }
  $file=Join-Path $out ("tut_$id.mp3")
  if ((Test-Path $file) -and -not $Force) { "skip  tut_$id (exists)"; continue }
  # v3 ignores speed and break tags: pacing comes from the sentence ends. The audio tag sets the delivery (energetic,
  # approachable, friendly) and is not spoken; stability 0.35 leans "creative" so the tag actually shows in the read
  $text='[cheerful] [upbeat] '+($lines[$id] -replace '\. ', '... ')
  $body=@{text=$text; model_id='eleven_v3'; voice_settings=@{stability=0.35; similarity_boost=0.8}} | ConvertTo-Json -Depth 4
  try {
    Invoke-WebRequest -Uri ("https://api.elevenlabs.io/v1/text-to-speech/$VOICE"+'?output_format=mp3_44100_96') -Method Post -Headers @{'xi-api-key'=$key; 'Content-Type'='application/json'; 'Accept'='audio/mpeg'} -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $file | Out-Null
    $chars+=$text.Length; "made  tut_$id  $((Get-Item $file).Length) bytes"
  } catch { $d=''; try { $rd=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $d=' '+$rd.ReadToEnd() } catch {}; "FAIL  tut_$id : $($_.Exception.Message)$d" }
}
# Vega's warnings and calls outside the tutorial: vega_<id>.mp3, read calm and clear
$vega=[ordered]@{
  fuel50 = "Fuel, fifty percent."
  fuel25 = "Fuel low, twenty-five percent."
  fuel10 = "Fuel critical, ten percent."
}
foreach ($id in $vega.Keys){
  if ($Only.Count -gt 0 -and $Only -notcontains $id) { continue }
  $file=Join-Path $out ("vega_$id.mp3")
  if ((Test-Path $file) -and -not $Force) { "skip  vega_$id (exists)"; continue }
  $text=($vega[$id] -replace '\. ', '... ')   # a straight read, no audio tag (an earlier version mangled every word with a bad regex)
  $body=@{text=$text; model_id='eleven_v3'; voice_settings=@{stability=0.5; similarity_boost=0.8}} | ConvertTo-Json -Depth 4
  try {
    Invoke-WebRequest -Uri ("https://api.elevenlabs.io/v1/text-to-speech/$VOICE"+'?output_format=mp3_44100_96') -Method Post -Headers @{'xi-api-key'=$key; 'Content-Type'='application/json'; 'Accept'='audio/mpeg'} -Body ([Text.Encoding]::UTF8.GetBytes($body)) -OutFile $file | Out-Null
    $chars+=$text.Length; "made  vega_$id  $((Get-Item $file).Length) bytes"
  } catch { $d=''; try { $rd=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $d=' '+$rd.ReadToEnd() } catch {}; "FAIL  vega_$id : $($_.Exception.Message)$d" }
}
"characters spent: $chars"
