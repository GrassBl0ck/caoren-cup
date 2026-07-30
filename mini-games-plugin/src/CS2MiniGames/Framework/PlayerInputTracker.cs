using CounterStrikeSharp.API;

namespace CS2MiniGames.Framework;

public sealed class PlayerInputTracker
{
    private readonly TimeSpan _horizontalInitialDelay;
    private readonly TimeSpan _repeatInterval;

    private PlayerButtons _previous;
    private HorizontalState _horizontalState;
    private TimeSpan? _nextLeftRepeat;
    private TimeSpan? _nextRightRepeat;
    private TimeSpan? _nextSoftDropRepeat;

    public PlayerInputTracker(
        int horizontalRepeatDelayMs = 150,
        int repeatIntervalMs = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizontalRepeatDelayMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repeatIntervalMs);

        _horizontalInitialDelay = TimeSpan.FromMilliseconds(horizontalRepeatDelayMs);
        _repeatInterval = TimeSpan.FromMilliseconds(repeatIntervalMs);
    }

    /// <summary>
    /// Converts the current physical button state into mini-game actions.
    /// </summary>
    /// <param name="current">The buttons held during this read.</param>
    /// <param name="now">
    /// A monotonic timestamp that must not be earlier than the value supplied to the previous read.
    /// </param>
    /// <returns>The actions produced during this read.</returns>
    public IReadOnlyCollection<MiniGameAction> Read(PlayerButtons current, TimeSpan now)
    {
        var actions = new List<MiniGameAction>();

        ReadHorizontal(current, now, actions);
        ReadRepeatingAction(
            current,
            PlayerButtons.Back,
            MiniGameAction.SoftDrop,
            now,
            _repeatInterval,
            ref _nextSoftDropRepeat,
            actions);

        ReadEdgeAction(current, PlayerButtons.Jump, MiniGameAction.HardDrop, actions);
        ReadEdgeAction(current, PlayerButtons.Use, MiniGameAction.RotateClockwise, actions);
        ReadEdgeAction(
            current,
            PlayerButtons.Reload,
            MiniGameAction.RotateCounterClockwise,
            actions);
        ReadEdgeAction(current, PlayerButtons.Forward, MiniGameAction.Hold, actions);
        ReadEdgeAction(current, PlayerButtons.Scoreboard, MiniGameAction.Exit, actions);

        _previous = current;
        return actions;
    }

    private void ReadHorizontal(
        PlayerButtons current,
        TimeSpan now,
        ICollection<MiniGameAction> actions)
    {
        var currentState = GetHorizontalState(current);

        if (currentState != _horizontalState)
        {
            _horizontalState = currentState;
            _nextLeftRepeat = null;
            _nextRightRepeat = null;

            if (currentState == HorizontalState.Left)
            {
                actions.Add(MiniGameAction.MoveLeft);
                _nextLeftRepeat = now + _horizontalInitialDelay;
            }
            else if (currentState == HorizontalState.Right)
            {
                actions.Add(MiniGameAction.MoveRight);
                _nextRightRepeat = now + _horizontalInitialDelay;
            }

            return;
        }

        if (currentState == HorizontalState.Left)
        {
            EmitRepeatedAction(
                MiniGameAction.MoveLeft,
                now,
                ref _nextLeftRepeat,
                actions);
        }
        else if (currentState == HorizontalState.Right)
        {
            EmitRepeatedAction(
                MiniGameAction.MoveRight,
                now,
                ref _nextRightRepeat,
                actions);
        }
    }

    private void ReadRepeatingAction(
        PlayerButtons current,
        PlayerButtons button,
        MiniGameAction action,
        TimeSpan now,
        TimeSpan initialDelay,
        ref TimeSpan? nextRepeat,
        ICollection<MiniGameAction> actions)
    {
        var held = IsHeld(current, button);
        var wasHeld = IsHeld(_previous, button);
        UpdateRepeatTimestamp(held, wasHeld, now, initialDelay, ref nextRepeat);

        if (held)
        {
            EmitNewOrRepeatedAction(wasHeld, action, now, ref nextRepeat, actions);
        }
    }

    private static void UpdateRepeatTimestamp(
        bool held,
        bool wasHeld,
        TimeSpan now,
        TimeSpan initialDelay,
        ref TimeSpan? nextRepeat)
    {
        if (!held)
        {
            nextRepeat = null;
        }
        else if (!wasHeld)
        {
            nextRepeat = now + initialDelay;
        }
    }

    private void EmitNewOrRepeatedAction(
        bool wasHeld,
        MiniGameAction action,
        TimeSpan now,
        ref TimeSpan? nextRepeat,
        ICollection<MiniGameAction> actions)
    {
        if (!wasHeld)
        {
            actions.Add(action);
            return;
        }

        EmitRepeatedAction(action, now, ref nextRepeat, actions);
    }

    private void EmitRepeatedAction(
        MiniGameAction action,
        TimeSpan now,
        ref TimeSpan? nextRepeat,
        ICollection<MiniGameAction> actions)
    {
        if (!nextRepeat.HasValue || now < nextRepeat.Value)
        {
            return;
        }

        actions.Add(action);
        do
        {
            nextRepeat += _repeatInterval;
        }
        while (nextRepeat <= now);
    }

    private void ReadEdgeAction(
        PlayerButtons current,
        PlayerButtons button,
        MiniGameAction action,
        ICollection<MiniGameAction> actions)
    {
        if (IsHeld(current, button) && !IsHeld(_previous, button))
        {
            actions.Add(action);
        }
    }

    private static bool IsHeld(PlayerButtons buttons, PlayerButtons button) =>
        (buttons & button) != 0;

    private static HorizontalState GetHorizontalState(PlayerButtons buttons)
    {
        var leftHeld = IsHeld(buttons, PlayerButtons.Moveleft);
        var rightHeld = IsHeld(buttons, PlayerButtons.Moveright);

        if (leftHeld == rightHeld)
        {
            return HorizontalState.None;
        }

        return leftHeld ? HorizontalState.Left : HorizontalState.Right;
    }

    private enum HorizontalState
    {
        None,
        Left,
        Right
    }
}
