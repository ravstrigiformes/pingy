# /// script
# requires-python = ">=3.10"
# dependencies = ["pyttsx3", "requests", "python-dotenv"]
# ///
"""
Claude Code Stop Hook — Text-to-Speech
Configurable via .env: set TTS_ENGINE to choose your voice.
Always falls back to pyttsx3 if the chosen engine fails.
"""

import sys
import json
import re
import os
import random
import base64
import tempfile
from pathlib import Path
from dotenv import load_dotenv

# Load .env from the project root (next to .claude/)
_env_path = Path(__file__).resolve().parent.parent.parent / ".env"
load_dotenv(_env_path)

_log_path = Path(__file__).resolve().parent.parent.parent / ".kris" / "hooks" / "tts.log"
_log_path.parent.mkdir(parents=True, exist_ok=True)

def _log(msg: str):
    from datetime import datetime
    with open(_log_path, "a", encoding="utf-8") as f:
        f.write(f"[{datetime.now().isoformat()}] {msg}\n")

def strip_markdown(text: str) -> str:
    text = re.sub(r"```[\s\S]*?```", " code block omitted ", text)
    text = re.sub(r"`[^`]+`", "", text)
    text = re.sub(r"#{1,6}\s+", "", text)
    text = re.sub(r"\*{1,3}(.*?)\*{1,3}", r"\1", text)
    text = re.sub(r"_{1,3}(.*?)_{1,3}", r"\1", text)
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    text = re.sub(r"!\[([^\]]*)\]\([^)]+\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"[-*_]{3,}", "", text)
    text = re.sub(r"\|", " ", text)
    text = re.sub(r"\s+", " ", text)
    return text.strip()

def truncate_for_speech(text: str, max_chars: int = 500) -> str:
    if len(text) <= max_chars:
        return text
    truncated = text[:max_chars]
    last_period = truncated.rfind(".")
    last_question = truncated.rfind("?")
    last_exclaim = truncated.rfind("!")
    cut_point = max(last_period, last_question, last_exclaim)
    if cut_point > max_chars // 2:
        return truncated[: cut_point + 1]
    return truncated + "..."

def get_last_assistant_text(transcript_path: str) -> str:
    try:
        with open(transcript_path, "r", encoding="utf-8") as f:
            lines = f.readlines()
    except (FileNotFoundError, PermissionError):
        return ""

    for line in reversed(lines):
        line = line.strip()
        if not line:
            continue
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue

        msg = entry.get("message", {})
        if msg.get("role") != "assistant":
            continue

        content = msg.get("content", [])
        if isinstance(content, str) and content.strip():
            return content
        if isinstance(content, list):
            text_parts = []
            for block in content:
                if isinstance(block, dict) and block.get("type") == "text":
                    text_parts.append(block["text"])
            if text_parts:
                return " ".join(text_parts)

    return ""

# ── Engine: InWorld ──────────────────────────────────────────────────────────

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

    except Exception as e:
        _log(f"InWorld failed: {e}")
        return False

# ── Engine: pyttsx3 (offline fallback) ───────────────────────────────────────

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

# ── Audio playback (for cloud TTS engines) ───────────────────────────────────

# Known ffplay paths per platform (add more as needed)
_FFPLAY_SEARCH_PATHS = [
    # Windows (winget install)
    r"C:\Users\mis\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build\bin\ffplay.exe",
]

def _find_ffplay() -> str:
    import shutil

    # Check PATH first
    found = shutil.which("ffplay")
    if found:
        return found

    # Check known locations
    for path in _FFPLAY_SEARCH_PATHS:
        if os.path.isfile(path):
            return path

    return ""

def _play_audio(filepath: str):
    import subprocess
    import platform

    system = platform.system()
    playback_timeout = 120  # 2 minutes — enough for long responses

    # Try ffplay first (works everywhere, handles mp3/wav/ogg)
    ffplay = _find_ffplay()
    if ffplay:
        subprocess.run(
            [ffplay, "-nodisp", "-autoexit", "-loglevel", "error", filepath],
            capture_output=True,
            timeout=playback_timeout,
        )
        return

    # Platform-specific fallbacks
    if system == "Darwin":
        subprocess.run(["afplay", filepath], capture_output=True, timeout=playback_timeout)
    elif system == "Linux":
        for cmd in [["mpv", "--no-video", filepath], ["aplay", filepath]]:
            try:
                subprocess.run(cmd, capture_output=True, timeout=playback_timeout)
                break
            except FileNotFoundError:
                continue
    else:
        # Windows fallback: SoundPlayer (WAV only)
        if filepath.endswith(".wav"):
            subprocess.run(
                ["powershell.exe", "-Command",
                 f'(New-Object Media.SoundPlayer "{filepath}").PlaySync()'],
                capture_output=True,
                timeout=playback_timeout,
            )

# ── Engine registry ──────────────────────────────────────────────────────────

ENGINES = {
    "inworld": speak_inworld,
    "pyttsx3": speak_pyttsx3,
}

# ── Speech intros/outros for variety ─────────────────────────────────────────

INTROS = [
    "",
    "",
    "",
    "Okay, so... ",
    "Alright. ",
    "Here's what I've got. ",
    "So, ",
    "Right. ",
    "Here's the thing. ",
    "Let me tell you. ",
]

OUTROS = [
    "",
    "",
    "",
    " That's the gist of it.",
    " And that's about it.",
    " Hope that helps.",
    " Let me know if you need more.",
    " There you go.",
]

# ── Main ─────────────────────────────────────────────────────────────────────

def main():
    try:
        data = json.loads(sys.stdin.read())
    except (json.JSONDecodeError, ValueError):
        return

    transcript_path = data.get("transcript_path", "")
    if not transcript_path:
        return

    response_text = get_last_assistant_text(transcript_path)
    if not response_text:
        return

    clean_text = strip_markdown(response_text)
    max_chars = int(os.environ.get("TTS_MAX_CHARS", "2000"))
    speech_text = truncate_for_speech(clean_text, max_chars)

    if not speech_text.strip():
        return

    # Add variety with random intros/outros (weighted toward empty for subtlety)
    intro = random.choice(INTROS)
    outro = random.choice(OUTROS)
    speech_text = f"{intro}{speech_text}{outro}"

    # Try the configured engine, then fall back to pyttsx3
    engine_name = os.environ.get("TTS_ENGINE", "inworld").lower()
    engine_fn = ENGINES.get(engine_name)

    if engine_fn and engine_fn(speech_text):
        return

    # Fallback: pyttsx3 (unless that's what already failed)
    if engine_name != "pyttsx3":
        _log(f"Falling back to pyttsx3 (engine '{engine_name}' failed)")
        speak_pyttsx3(speech_text)

if __name__ == "__main__":
    main()
