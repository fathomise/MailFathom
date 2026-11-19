// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers what a task may be anchored to and the words the anchor is drawn with.</summary>
public sealed class TaskAnchorTests
{
    private static readonly EmailThreadId ThreadId = EmailThreadId.Create(Guid.NewGuid());

    private static readonly CalendarEventId EventId = CalendarEventId.Create(Guid.NewGuid());

    /// <summary>A thread anchor names the thread and no event.</summary>
    [Fact]
    public void Thread_ASpecifiedThread_NamesTheThreadAndNoEvent()
    {
        // Act
        var anchor = TaskAnchor.Thread(ThreadId, "  Tender reply  ");

        // Assert
        Assert.Equal(TaskAnchorKind.Thread, anchor.Kind);
        Assert.Equal(ThreadId, anchor.ThreadId);
        Assert.Null(anchor.EventId);
        Assert.Equal("Tender reply", anchor.Label);
    }

    /// <summary>An event anchor names the event and no thread.</summary>
    [Fact]
    public void Event_ASpecifiedEvent_NamesTheEventAndNoThread()
    {
        // Act
        var anchor = TaskAnchor.Event(EventId, "Kick-off");

        // Assert
        Assert.Equal(TaskAnchorKind.Event, anchor.Kind);
        Assert.Equal(EventId, anchor.EventId);
        Assert.Null(anchor.ThreadId);
        Assert.Equal("Kick-off", anchor.Label);
    }

    /// <summary>An anchor with no words cannot be drawn.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Thread_ABlankLabel_IsRefused(string label)
    {
        // Act
        var anchoring = () => TaskAnchor.Thread(ThreadId, label);

        // Assert
        Assert.Throws<ArgumentException>(anchoring);
    }

    /// <summary>An event anchor with no words cannot be drawn either.</summary>
    [Fact]
    public void Event_ABlankLabel_IsRefused()
    {
        // Act
        var anchoring = () => TaskAnchor.Event(EventId, " ");

        // Assert
        Assert.Throws<ArgumentException>(anchoring);
    }

    /// <summary>The stored bound holds after trimming.</summary>
    [Fact]
    public void Thread_ALabelPastTheStoredBound_IsRefused()
    {
        // Act
        var anchoring = () => TaskAnchor.Thread(ThreadId, new string('x', TaskAnchor.MaximumLabelLength + 1));

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(anchoring);
    }

    /// <summary>The struct defaults name nothing, so no anchor is built from them.</summary>
    [Fact]
    public void Thread_TheDefaultThread_IsRefused()
    {
        // Act
        var anchoring = () => TaskAnchor.Thread(default, "Tender reply");

        // Assert
        Assert.Throws<ArgumentException>(anchoring);
    }

    /// <summary>The struct defaults name nothing, so no anchor is built from them.</summary>
    [Fact]
    public void Event_TheDefaultEvent_IsRefused()
    {
        // Act
        var anchoring = () => TaskAnchor.Event(default, "Kick-off");

        // Assert
        Assert.Throws<ArgumentException>(anchoring);
    }
}
