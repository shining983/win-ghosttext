using System.Runtime.InteropServices;
using WinGhostText.App.Native;
using WinGhostText.Core;

namespace WinGhostText.App.Input;

internal static class TextInjector
{
    public static void Accept(CompletionSuggestion suggestion, bool appendSpace)
    {
        if (suggestion.ReplaceTypedPrefix)
        {
            // Delete typed misspelling then insert correction.
            Backspace(suggestion.TypedPrefix.Length);
            TypeText(suggestion.FullWord);
        }
        else
        {
            TypeText(suggestion.InsertionText);
        }

        if (appendSpace)
        {
            TypeText(" ");
        }
    }

    public static void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[text.Length * 2];
        var idx = 0;
        foreach (var ch in text)
        {
            inputs[idx++] = UnicodeKey(ch, keyUp: false);
            inputs[idx++] = UnicodeKey(ch, keyUp: true);
        }

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void Backspace(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var inputs = new NativeMethods.INPUT[count * 2];
        for (var i = 0; i < count; i++)
        {
            inputs[i * 2] = VKey(VirtualKeys.Back, keyUp: false);
            inputs[i * 2 + 1] = VKey(VirtualKeys.Back, keyUp: true);
        }

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT UnicodeKey(char ch, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };

    private static NativeMethods.INPUT VKey(int vk, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        }
    };
}
