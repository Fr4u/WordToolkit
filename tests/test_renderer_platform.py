import contextlib
import sys
import types

from wordtoolkit.engine import renderer


def test_windows_app_path_returns_none_outside_windows(monkeypatch):
    monkeypatch.setattr(renderer.sys, "platform", "linux")

    assert renderer._windows_app_path("soffice.exe") is None


def test_windows_app_path_checks_machine_and_user_hives(monkeypatch, tmp_path):
    executable = tmp_path / "soffice.exe"
    executable.write_bytes(b"stub")
    calls = []

    def open_key(hive, key):
        calls.append((hive, key))
        if len(calls) < 3:
            raise OSError("not registered in this hive")
        return contextlib.nullcontext(key)

    fake_winreg = types.SimpleNamespace(
        HKEY_LOCAL_MACHINE=1,
        HKEY_CURRENT_USER=2,
        OpenKey=open_key,
        QueryValueEx=lambda _handle, _name: (str(executable), 1),
    )
    monkeypatch.setitem(sys.modules, "winreg", fake_winreg)
    monkeypatch.setattr(renderer.sys, "platform", "win32")

    assert renderer._windows_app_path("soffice.exe") == str(executable)
    assert [hive for hive, _key in calls] == [1, 1, 2]


def test_subprocess_flags_follow_platform(monkeypatch):
    monkeypatch.setattr(renderer.sys, "platform", "linux")
    assert renderer._subprocess_flags() == 0

    monkeypatch.setattr(renderer.sys, "platform", "win32")
    monkeypatch.setattr(renderer.subprocess, "CREATE_NO_WINDOW", 0x08000000, raising=False)
    assert renderer._subprocess_flags() == 0x08000000
