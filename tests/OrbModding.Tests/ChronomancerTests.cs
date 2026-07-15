using System;
using System.Reflection;
using BepInEx.Configuration;
using OrbChronomancer;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xunit;

namespace OrbModding.Tests;

public sealed class ChronomancerTests
{
    [Fact]
    public void AdditiveNonGameplayScene_DoesNotResetActiveGameplaySpeed()
    {
        SceneManager.ActiveScene = new Scene("Main");
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        var plugin = new Plugin();

        try
        {
            Invoke(plugin, "Awake");
            Invoke(plugin, "ApplyMultiplier", 2.0f, "test");
            Assert.Equal(2.0f, Time.timeScale);

            Invoke(plugin, "OnSceneLoaded", new Scene("Overlay"), LoadSceneMode.Additive);

            Assert.Equal(2.0f, Time.timeScale);
        }
        finally
        {
            Invoke(plugin, "OnDestroy");
        }
    }

    [Fact]
    public void EightX_RemainsGuardedUntilExplicitlyEnabled()
    {
        SceneManager.ActiveScene = new Scene("Main");
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        var plugin = new Plugin();

        try
        {
            Invoke(plugin, "Awake");
            Invoke(plugin, "ApplyMultiplier", 8.0f, "test");
            Assert.Equal(4.0f, Time.timeScale);

            GetConfigEntry<bool>(plugin, "_allowExperimentalEightX").Value = true;
            GetConfigEntry<float>(plugin, "_maximumMultiplier").Value = 8.0f;
            Invoke(plugin, "ApplyMultiplier", 8.0f, "test");
            Assert.Equal(8.0f, Time.timeScale);
        }
        finally
        {
            Invoke(plugin, "OnDestroy");
        }
    }

    [Fact]
    public void SaveLoadSafety_ForcesOneX()
    {
        SceneManager.ActiveScene = new Scene("Main");
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        var plugin = new Plugin();

        try
        {
            Invoke(plugin, "Awake");
            Invoke(plugin, "ApplyMultiplier", 2.0f, "test");

            Invoke(plugin, "EnterSaveLoadSafety");

            Assert.Equal(1.0f, Time.timeScale);
        }
        finally
        {
            Invoke(plugin, "OnDestroy");
        }
    }

    [Fact]
    public void NonGameplayScene_RefusesAcceleration()
    {
        SceneManager.ActiveScene = new Scene("Title");
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
        var plugin = new Plugin();

        try
        {
            Invoke(plugin, "Awake");
            Invoke(plugin, "ApplyMultiplier", 4.0f, "test");

            Assert.Equal(1.0f, Time.timeScale);
            Assert.Equal(0.02f, Time.fixedDeltaTime);
        }
        finally
        {
            Invoke(plugin, "OnDestroy");
        }
    }

    [Fact]
    public void Destroy_RestoresCapturedTimingBaseline()
    {
        SceneManager.ActiveScene = new Scene("Main");
        Time.timeScale = 0.75f;
        Time.fixedDeltaTime = 0.03f;
        var plugin = new Plugin();

        Invoke(plugin, "Awake");
        Invoke(plugin, "ApplyMultiplier", 2.0f, "test");
        Assert.Equal(1.5f, Time.timeScale);

        Invoke(plugin, "OnDestroy");

        Assert.Equal(0.75f, Time.timeScale);
        Assert.Equal(0.03f, Time.fixedDeltaTime);
    }

    private static object? Invoke(object instance, string methodName, params object[] arguments)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(instance, arguments);
    }

    private static ConfigEntry<T> GetConfigEntry<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<ConfigEntry<T>>(field!.GetValue(instance));
    }
}
