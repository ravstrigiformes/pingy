# /// script
# requires-python = ">=3.10"
# dependencies = ["pyttsx3", "requests", "python-dotenv"]
# ///
"""
Claude Code Notification Hook — Text-to-Speech
Speaks notification messages using the configured TTS engine.
Falls back to pyttsx3 if the chosen engine fails.
"""

import sys
import json
import os
import base64
import tempfile
from pathlib import Path
from dotenv import load_dotenv

_env_path = Path(__file__).resolve().parent.parent.parent / ".env"
load_dotenv(_env_path)

def speak_inworld(text: str) -> bool:
    api_key = os.environ.get("INWORLD_API_KEY", "REJqUnlIQTltdDBCU2daM3BMQWZ1b09PdVpmeVN6MTA6VThucmt1RmRBaHlNRFg3R3d1UGsyWVNjb25xb2lybXdWdXlPQWRjUDhHZk56c0ZXSFNiT1k0WGFMTDVVQ0pXMw==")
    if not api_key:
        return False

    try:
        import requests

        response = requests.post(
            "https://api.inworld.ai/tts/v1/voice",
            headers={
                "Authorization": f"Basic {api_key}",
                "Content-Type": "application/json",
            },
            json={
                "text": text,
                "voiceId": os.environ.get("INWORLD_VOICE", "Olivia"),
                "modelId": os.environ.get("INWORLD_MODEL", "inworld-tts-1.5-max"),
            },
            timeout=15,
        )
        response.raise_for_status()

        audio_b64 = response.json().get("audioContent", "")
        if not audio_b64:
            return False

        audio_data = base64.b64decode(audio_b64)

        tmp = tempfile.NamedTemporaryFile(suffix=".mp3", delete=False)
        tmp.write(audio_data)
        tmp.close()

        _play_audio(tmp.name)
        os.unlink(tmp.name)
        return True

    except Exception:
        return False

def _find_ffplay() -> str:
    import shutil

    found = shutil.which("ffplay")
    if found:
        return found

    known = r"C:\Users\mis\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build\bin\ffplay.exe"
    if os.path.isfile(known):
        return known

    return ""

def _play_audio(filepath: str):
    import subprocess
    import platform

    ffplay = _find_ffplay()
    if ffplay:
        subprocess.run(
            [ffplay, "-nodisp", "-autoexit", "-loglevel", "error", filepath],
            capture_output=True,
            timeout=30,
        )
        return

    system = platform.system()
    if system == "Darwin":
        subprocess.run(["afplay", filepath], capture_output=True, timeout=30)
    elif system == "Linux":
        for cmd in [["mpv", "--no-video", filepath], ["aplay", filepath]]:
            try:
                subprocess.run(cmd, capture_output=True, timeout=30)
                break
            except FileNotFoundError:
                continue

def speak_pyttsx3(text: str) -> bool:
    try:
        import pyttsx3

        engine = pyttsx3.init()

        rate_offset = int(os.environ.get("PYTTSX3_RATE", "-30"))
        engine.setProperty("rate", engine.getProperty("rate") + rate_offset)
        engine.setProperty("volume", 1.0)

        preferred = os.environ.get("PYTTSX3_VOICE", "zira").lower()
        voices = engine.getProperty("voices")
        for voice in voices:
            if preferred in voice.name.lower():
                engine.setProperty("voice", voice.id)
                break

        engine.say(text)
        engine.runAndWait()
        return True
    except Exception:
        return False

ENGINES = {
    "inworld": speak_inworld,
    "pyttsx3": speak_pyttsx3,
}

ATTENTION_PHRASES = [
    "Hey, I need you over here.",
    "Whenever you're ready, I've got something for you.",
    "Quick heads up, I need your input.",
    "Paging you. Come take a look.",
    "I'm waiting on you for this one.",
    "Got a moment? I need your eyes on something.",
    "Knock knock. Your attention please.",
    "Just a nudge. I need you back here.",
    "Over here! I've got something to show you.",
    "Pardon the interruption, but I need you.",
    "Hey, don't forget about me over here.",
    "Ready when you are. No rush... okay maybe a little rush.",
    "I'll be here when you're back. Just saying.",
    "Ding ding! You're needed.",
    "Sorry to bug you, but this needs your attention.",
]

def main():
    try:
        data = json.loads(sys.stdin.read())
    except (json.JSONDecodeError, ValueError):
        return

    import random
    message = random.choice(ATTENTION_PHRASES)

    engine_name = os.environ.get("TTS_ENGINE", "inworld").lower()
    engine_fn = ENGINES.get(engine_name)

    if engine_fn and engine_fn(message):
        return

    if engine_name != "pyttsx3":
        speak_pyttsx3(message)

if __name__ == "__main__":
    main()
