using System;
using UnityEngine.SceneManagement;

namespace OrbMentor;

internal static class MentorGameplayScene
{
    internal static bool IsActive() =>
        string.Equals(SceneManager.GetActiveScene().name, "Main", StringComparison.Ordinal);
}
