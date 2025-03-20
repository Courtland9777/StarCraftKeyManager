﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StarCraftKeyManager.Interfaces;
using StarCraftKeyManager.Models;
using StarCraftKeyManager.Services;

namespace StarCraftKeyManager.Tests.Integration;

public class ProcessMonitorIntegrationTests
{
    private readonly Mock<ILogger<ProcessMonitorService>> _mockLogger;
    private readonly Mock<IProcessEventWatcher> _mockProcessEventWatcher;
    private readonly ProcessMonitorService _processMonitorService;

    public ProcessMonitorIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<ProcessMonitorService>>();
        _mockProcessEventWatcher = new Mock<IProcessEventWatcher>();

        var mockSettings = new AppSettings
        {
            ProcessMonitor = new ProcessMonitorSettings { ProcessName = "starcraft.exe" },
            KeyRepeat = new KeyRepeatSettings
            {
                Default = new KeyRepeatState { RepeatSpeed = 31, RepeatDelay = 1000 },
                FastMode = new KeyRepeatState { RepeatSpeed = 20, RepeatDelay = 500 }
            }
        };

        var mockOptionsMonitor = new Mock<IOptionsMonitor<AppSettings>>();
        mockOptionsMonitor.Setup(o => o.CurrentValue).Returns(mockSettings);
        var optionsMonitor = mockOptionsMonitor.Object;

        _processMonitorService = new ProcessMonitorService(
            _mockLogger.Object,
            optionsMonitor,
            _mockProcessEventWatcher.Object
        );
    }

    [Fact]
    public async Task StartAsync_ShouldBeginMonitoring_WhenInvoked()
    {
        var cts = new CancellationTokenSource();
        var monitorTask = _processMonitorService.StartAsync(cts.Token);
        await Task.Delay(500, cts.Token);

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o != null! && o.ToString()!.Contains("Process monitor service started")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);

        await cts.CancelAsync();
        await monitorTask;
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldApplyFastMode_WhenStarCraftStarts()
    {
        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4688, 1234, "starcraft.exe")
        );

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o != null! && o.ToString()!.Contains("Applying key repeat settings: FastMode")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldRestoreDefaultSettings_WhenStarCraftStops()
    {
        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4688, 1234, "starcraft.exe")
        );

        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4689, 1234, "starcraft.exe")
        );

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o.ToString()!.Contains("Restoring default key repeat settings")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public void ConfigurationUpdated_ShouldApplyNewSettings_WhenConfigurationChanges()
    {
        var newSettings = new AppSettings
        {
            ProcessMonitor = new ProcessMonitorSettings { ProcessName = "newgame.exe" },
            KeyRepeat = new KeyRepeatSettings
            {
                Default = new KeyRepeatState { RepeatSpeed = 25, RepeatDelay = 750 },
                FastMode = new KeyRepeatState { RepeatSpeed = 15, RepeatDelay = 400 }
            }
        };

        var mockOptionsMonitor = new Mock<IOptionsMonitor<AppSettings>>();
        mockOptionsMonitor.Setup(o => o.CurrentValue).Returns(newSettings);

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o != null! && o.ToString()!.Contains("Configuration updated")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldNotApplySettings_WhenProcessIsNotStarCraft()
    {
        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4688, 5678, "notepad.exe")
        );

        await Task.Delay(100);

        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<object>(o => o.ToString()!.Contains("Applying key repeat settings")),
                null,
                It.IsAny<Func<object, Exception?, string>>()
            ), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ShouldLogError_WhenProcessWatcherFailsToStart()
    {
        _mockProcessEventWatcher.Setup(w => w.Start()).Throws(new Exception("Failed to start watcher"));

        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockLogger.Verify(log => log.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<object>(o => o.ToString()!.Contains("Failed to start watcher")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldIgnoreUnknownEventId_WhenInvalidEventIsReceived()
    {
        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(9999, 1234, "starcraft.exe")
        );

        await Task.Delay(100);

        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<object>(o => o.ToString()!.Contains("Unknown event ID")),
                null,
                It.IsAny<Func<object, Exception?, string>>()
            ), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ShouldApplyDefaultSettings_OnStartup()
    {
        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o.ToString()!.Contains("Applying key repeat settings: Default")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldHandleMultipleProcessStarts_WhenStarCraftStartsMultipleTimes()
    {
        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4688, 1234, "starcraft.exe")
        );
        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4688, 5678, "starcraft.exe")
        );

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o.ToString()!.Contains("Applying key repeat settings: FastMode")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }

    [Fact]
    public async Task ProcessEventOccurred_ShouldHandleMultipleProcessExits_WhenStarCraftStopsMultipleTimes()
    {
        await _processMonitorService.StartAsync(CancellationToken.None);

        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4689, 1234, "starcraft.exe")
        );
        _mockProcessEventWatcher.Raise(
            w => w.ProcessEventOccurred += null,
            new ProcessEventArgs(4689, 5678, "starcraft.exe")
        );

        _mockLogger.Verify(log => log.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<object>(o => o.ToString()!.Contains("Restoring default key repeat settings")),
            null,
            It.IsAny<Func<object, Exception?, string>>()
        ), Times.Once);
    }
}