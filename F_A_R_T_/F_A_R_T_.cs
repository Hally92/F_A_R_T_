using BepInEx;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using REPOLib.Modules;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[BepInPlugin("com.HalHally.fart_mod", "F.A.R.T.", "1.0.2")]
[BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
public class FartMod : BaseUnityPlugin
{
    public static List<AudioClip> FartClips = new List<AudioClip>();
    private static float _lastFartTime;

    public static ConfigEntry<int> MasterVolume;
    public static ConfigEntry<bool> RandomPitchEnabled;
    public static ConfigEntry<float> PitchVariation;
    public static ConfigEntry<bool> TumbleEnabled, JumpEnabled, SlidingEnabled, SprintEnabled, CrouchEnabled;
    public static ConfigEntry<int> TumbleChance, JumpChance, SlidingChance, SprintChance, CrouchChance;

    public static NetworkedEvent FartNetworkEvent;
    internal static new BepInEx.Logging.ManualLogSource Logger;

    private static readonly AccessTools.FieldRef<PlayerController, bool> JumpImpulseRef =
        AccessTools.FieldRefAccess<PlayerController, bool>("JumpImpulse");
    private static readonly AccessTools.FieldRef<PlayerAvatar, bool> IsTumblingRef =
        AccessTools.FieldRefAccess<PlayerAvatar, bool>("isTumbling");

    void Awake()
    {
        Logger = base.Logger;
        InitConfig();
        LoadAudioFiles();
        new Harmony("com.HalHally.fart_mod").PatchAll();
        FartNetworkEvent = new NetworkedEvent("FartEvent_UniqueKey", OnFartReceived);
    }

    public static void TryFart(Vector3 pos, bool enabled, int chance)
    {
        if (!enabled || !PhotonNetwork.InRoom) return;
        if (Time.time - _lastFartTime < 0.1f) return;

        if (Random.Range(0, 100) < chance)
        {
            _lastFartTime = Time.time;
            FartNetworkEvent.RaiseEvent(
                new object[] { pos, PhotonNetwork.LocalPlayer.ActorNumber },
                NetworkingEvents.RaiseAll,
                SendOptions.SendReliable);
        }
    }

    private static void OnFartReceived(EventData photonEvent)
    {
        var data = (object[])photonEvent.CustomData;
        PlayLocalFartSound((Vector3)data[0]);
        AlertNearbyEnemies((Vector3)data[0], (int)data[1]);
    }

    private static void PlayLocalFartSound(Vector3 pos)
    {
        if (FartClips.Count == 0 || AudioManager.instance == null) return;

        GameObject audioObj = Instantiate(AudioManager.instance.AudioDefault, pos + Vector3.up * 1.2f, Quaternion.identity);
        try { REPOLib.Modules.Utilities.FixAudioMixerGroups(audioObj); } catch { }

        AudioSource source = audioObj.GetComponent<AudioSource>() ?? audioObj.AddComponent<AudioSource>();
        AudioClip clip = FartClips[Random.Range(0, FartClips.Count)];
        source.clip = clip;
        source.volume = MasterVolume.Value / 100f;

        // mp3特有の減衰バグ対策
        if (source.spatialBlend > 0f)
            source.minDistance = Mathf.Max(source.minDistance, 3.0f);

        if (RandomPitchEnabled.Value)
            source.pitch = Mathf.Clamp(1.0f + Random.Range(-PitchVariation.Value, PitchVariation.Value), 0.2f, 1.8f);

        if (AudioManager.instance.SoundsParent != null)
            audioObj.transform.SetParent(AudioManager.instance.SoundsParent);

        source.Play();
        Destroy(audioObj, clip.length + 0.5f);
    }

    public static void AlertNearbyEnemies(Vector3 fartPosition, int actorNumber)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (EnemyHunter hunter in GameObject.FindObjectsOfType<EnemyHunter>())
        {
            if (Vector3.Distance(fartPosition, hunter.transform.position) > 20f) continue;
            try
            {
                AccessTools.Field(typeof(EnemyHunter), "shootFast").SetValue(hunter, true);
                AccessTools.Field(typeof(EnemyHunter), "investigatePoint").SetValue(hunter, fartPosition);
                AccessTools.Field(typeof(EnemyHunter), "investigatePathfindOnly").SetValue(hunter, false);
                AccessTools.Method(typeof(EnemyHunter), "InvestigateTransformGet").Invoke(hunter, null);

                var pv = (PhotonView)AccessTools.Field(typeof(EnemyHunter), "photonView").GetValue(hunter);
                if (SemiFunc.IsMultiplayer() && pv != null)
                    pv.RPC("UpdateInvestigationPoint", RpcTarget.Others, fartPosition);

                AccessTools.Method(typeof(EnemyHunter), "UpdateState")
                    .Invoke(hunter, new object[] { EnemyHunter.State.Investigate });
            }
            catch (System.Exception ex) { Logger.LogError(ex.Message); }
        }
    }

    private static PlayerStateTracker GetTracker(PlayerController pc)
    {
        PlayerStateTracker t = pc.GetComponent<PlayerStateTracker>();
        return t ? t : pc.gameObject.AddComponent<PlayerStateTracker>();
    }

    public class PlayerStateTracker : MonoBehaviour
    {
        public bool lastSliding, lastSprinting, lastCrouching, lastTumbling, lastJumpImpulse;
    }

    private void InitConfig()
    {
        MasterVolume = Config.Bind("General", "Volume", 100, new ConfigDescription("Volume", new AcceptableValueRange<int>(0, 100)));
        TumbleEnabled = Config.Bind("Tumble", "Enabled", true);
        TumbleChance = Config.Bind("Tumble", "FartRate", 20, new ConfigDescription("Chance of farting when tumbling", new AcceptableValueRange<int>(0, 100)));
        JumpEnabled = Config.Bind("Jump", "Enabled", true);
        JumpChance = Config.Bind("Jump", "FartRate", 5, new ConfigDescription("Chance of farting when jumping", new AcceptableValueRange<int>(0, 100)));
        SlidingEnabled = Config.Bind("Sliding", "Enabled", true);
        SlidingChance = Config.Bind("Sliding", "FartRate", 15, new ConfigDescription("Chance of farting when sliding", new AcceptableValueRange<int>(0, 100)));
        SprintEnabled = Config.Bind("Sprint", "Enabled", false);
        SprintChance = Config.Bind("Sprint", "FartRate", 5, new ConfigDescription("Chance of farting while sprinting", new AcceptableValueRange<int>(0, 100)));
        CrouchEnabled = Config.Bind("Crouch", "Enabled", true);
        CrouchChance = Config.Bind("Crouch", "FartRate", 10, new ConfigDescription("Chance of farting when crouching", new AcceptableValueRange<int>(0, 100)));
        RandomPitchEnabled = Config.Bind("Audio", "RandomPitchEnabled", true);
        PitchVariation = Config.Bind("Audio", "PitchVariation", 0.50f, new ConfigDescription("Pitch variation range", new AcceptableValueRange<float>(0f, 1f)));
    }

    private void LoadAudioFiles()
    {
        var pluginFolder = Path.GetDirectoryName(Info.Location);
        foreach (var file in Directory.GetFiles(pluginFolder, "*.mp3"))
            StartCoroutine(LoadFartSound(file));
    }

    IEnumerator LoadFartSound(string path)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
                FartClips.Add(DownloadHandlerAudioClip.GetContent(www));
        }
    }

    [HarmonyPatch(typeof(PlayerController), "Update")]
    public static class JumpMonitorPatch
    {
        static void Postfix(PlayerController __instance)
        {
            if (__instance.playerAvatarScript == null || !__instance.playerAvatarScript.photonView.IsMine) return;
            var jumpImpulse = JumpImpulseRef(__instance);
            PlayerStateTracker tracker = GetTracker(__instance);
            if (jumpImpulse && !tracker.lastJumpImpulse)
                TryFart(__instance.transform.position, JumpEnabled.Value, JumpChance.Value);
            tracker.lastJumpImpulse = jumpImpulse;
        }
    }

    [HarmonyPatch(typeof(PlayerController), "ChangeState")]
    public static class ActionMonitorPatch
    {
        static void Postfix(PlayerController __instance)
        {
            if (__instance.playerAvatarScript == null || !__instance.playerAvatarScript.photonView.IsMine) return;
            PlayerStateTracker tracker = GetTracker(__instance);
            var currentTumbling = IsTumblingRef(__instance.playerAvatarScript);

            if (currentTumbling && !tracker.lastTumbling) TryFart(__instance.transform.position, TumbleEnabled.Value, TumbleChance.Value);
            if (__instance.Sliding && !tracker.lastSliding) TryFart(__instance.transform.position, SlidingEnabled.Value, SlidingChance.Value);
            if (__instance.sprinting && !tracker.lastSprinting) TryFart(__instance.transform.position, SprintEnabled.Value, SprintChance.Value);
            if (__instance.Crouching && !tracker.lastCrouching) TryFart(__instance.transform.position, CrouchEnabled.Value, CrouchChance.Value);

            tracker.lastTumbling = currentTumbling;
            tracker.lastSliding = __instance.Sliding;
            tracker.lastSprinting = __instance.sprinting;
            tracker.lastCrouching = __instance.Crouching;
        }
    }
}
