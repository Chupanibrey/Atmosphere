using System;
using UnityEngine;

/// <summary>
/// Односторонние стены прототипа видны лучу только с включёнными задними гранями.
/// Вложенные блоки возвращают флаг как было.
/// </summary>
readonly struct BackfaceQueries : IDisposable
{
    readonly bool _previous;

    BackfaceQueries(bool previous)
    {
        _previous = previous;
    }

    public static BackfaceQueries Begin()
    {
        bool previous = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        return new BackfaceQueries(previous);
    }

    public void Dispose()
    {
        Physics.queriesHitBackfaces = _previous;
    }
}
