using System;

namespace Mapbox.Net;

/// <summary>An error raised by the map engine while executing a command.</summary>
public class MapException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MapException(string message, string? command = null, string? scriptStack = null, Exception? inner = null)
        : base(message, inner)
    {
        Command = command;
        ScriptStack = scriptStack;
    }

    /// <summary>Bridge command that failed.</summary>
    public string? Command { get; }

    /// <summary>JavaScript stack trace, when available.</summary>
    public string? ScriptStack { get; }
}

/// <summary>The map could not be initialized (engine not loaded, invalid token, WebGL unavailable…).</summary>
public sealed class MapInitializationException : MapException
{
    /// <summary>Creates the exception.</summary>
    public MapInitializationException(string message) : base(message) { }
}
