using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Textures;
using Meitou.Game.Host;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace CharTweak3;

/// <summary>Reuses the native transparent editor stage with live world bones. Readback happens after the frame, on the render thread.</summary>
internal static class PhotoRendering
{
    internal sealed class Shot(object player, WorldSession session, CharacterId character)
    {
        internal readonly object Player = player;
        internal readonly WorldSession Session = session;
        internal readonly CharacterId Character = character;
        internal bool Active = true, Ready, Drawn;
        internal float Yaw = .25f, Elevation = .08f, Distance = 42;
        internal bool Head;
        internal Vector2 Pan;
        internal string? Error;
        internal object? Renderer;
        internal EditorStageRequest Request;
        internal TaskCompletionSource<RgbaImage>? Capture;

        internal EditorStageRequest CreateRequest(long key) => new(key, PortraitFiles.Size, PortraitFiles.Size,
            Yaw, Elevation, Distance, Head, .55f, 0);

        internal void ResetCamera()
        {
            Yaw = .25f; Elevation = .08f; Distance = 42; Head = false; Pan = Vector2.Zero;
        }
    }
    private static readonly ConditionalWeakTable<object, Shot> Players = new(), Renderers = new();
    private static FieldInfo s_player = null!, s_session = null!, s_loopHost = null!, s_keys = null!, s_painter = null!;
    private static PropertyInfo s_characters = null!, s_stage = null!, s_texture = null!, s_gpu = null!, s_view = null!;
    private static PropertyInfo? s_background;
    private static MethodInfo s_key = null!, s_draw = null!, s_resolve = null!, s_setTexture = null!, s_bone = null!;
    private static Action<string> s_log = null!;
    internal static bool Available { get; private set; }

    internal static void Install(ModContext context)
    {
        s_log = context.LogError;
        var patches = new List<(MethodInfo Target, MethodInfo Patch)>();
        try
        {
            var assembly = typeof(IMod).Assembly;
            var host = assembly.GetType("Meitou.Game.GameHost", true)!;
            var player = assembly.GetType("Meitou.Game.PlayerInterface", true)!;
            var renderer = assembly.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer", true)!;
            var loop = assembly.GetType("Meitou.Game.Host.GameLoop", true)!;
            s_player = AccessTools.Field(host, "Player"); s_session = AccessTools.Field(player, "Session");
            s_loopHost = loop.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == host);
            s_keys = AccessTools.Field(host, "_portraitKeys"); s_painter = AccessTools.Field(player, "UiPainter");
            s_characters = AccessTools.Property(host, "ActiveCharacters"); s_key = AccessTools.Method(host, "DrawKey", [typeof(CharacterId)]);
            s_stage = AccessTools.Property(renderer, "StageRequest"); s_texture = AccessTools.Property(renderer, "StageTexture");
            s_view = AccessTools.Property(renderer, "StageView"); s_gpu = AccessTools.Property(renderer, "Gpu");
            s_draw = AccessTools.Method(renderer, "DrawStage", Type.EmptyTypes); s_resolve = AccessTools.Method(renderer, "ResolveStage", Type.EmptyTypes);
            s_setTexture = AccessTools.Method(player, "SetUiTexture", [typeof(string), typeof(GpuTexture), typeof(SampledTexture)]);
            s_bone = AccessTools.Method(renderer, "BonePosition", [typeof(long), typeof(string)]);
            var camera = AccessTools.DeclaredMethod(renderer, "StageCamera");
            s_background = assembly.GetType("Meitou.Game.Rendering.Characters.PreviewStudio") is { } studio ? AccessTools.Property(studio, "Background") : null;
            if (s_player is null || s_session is null || s_keys?.FieldType != typeof(HashSet<long>) || s_painter is null
                || s_characters is null || s_key?.ReturnType != typeof(long) || s_stage?.PropertyType != typeof(EditorStageRequest?)
                || s_texture is null || s_view is null || s_gpu is null || s_draw is null || s_resolve is null || s_setTexture is null
                || s_bone?.ReturnType != typeof(Vector3?) || camera?.ReturnType != typeof(ValueTuple<Vector3, Vector3>))
                throw new NotSupportedException("The native photo rendering contract is unavailable.");
            void Add(Type type, string method, string patch, bool prefix)
            {
                var target = AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.Name, method);
                var handler = AccessTools.DeclaredMethod(typeof(PhotoRendering), patch);
                patches.Add((target, handler));
                if (prefix) context.Harmony.Patch(target, prefix: new HarmonyMethod(handler));
                else context.Harmony.Patch(target, postfix: new HarmonyMethod(handler));
            }
            Add(host, "FillDrawList", nameof(Prepare), true);
            Add(host, "GatherPortraitKeys", nameof(KeepPosed), false);
            Add(renderer, "DrawPortraits", nameof(Draw), false);
            Add(renderer, "StageCamera", nameof(Camera), false);
            Add(loop, "DrawFrame", nameof(EndFrame), false);
            Available = true;
        }
        catch (Exception e)
        {
            foreach (var (target, patch) in patches) context.Harmony.Unpatch(target, patch);
            context.LogError("Photo prototype disabled: " + e.GetBaseException().Message);
        }
    }

    internal static Shot Open(object player, CharacterId character)
    {
        if (Players.TryGetValue(player, out var old)) Close(old);
        var shot = new Shot(player, (WorldSession)s_session.GetValue(player)!, character);
        Players.Add(player, shot);
        return shot;
    }

    internal static void Close(Shot shot)
    {
        shot.Active = false; shot.Ready = false; shot.Capture?.TrySetCanceled(); shot.Capture = null;
        if (shot.Renderer is { } renderer && s_stage.GetValue(renderer) is EditorStageRequest request && request == shot.Request)
            s_stage.SetValue(renderer, null);
        (s_painter.GetValue(shot.Player) as UiPainter)?.Forget(PhotoLayout.Texture);
        Players.Remove(shot.Player);
    }

    internal static Task<RgbaImage> Capture(Shot shot)
    {
        if (!Valid(shot) || !shot.Ready || shot.Error is not null || shot.Capture is not null)
            return Task.FromException<RgbaImage>(new InvalidOperationException("Wait for the character preview before taking a picture."));
        shot.Capture = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return shot.Capture.Task;
    }

    private static bool Valid(Shot shot) => shot.Active && ReferenceEquals(s_session.GetValue(shot.Player), shot.Session)
        && PersonalityUiBridge.LeadFor(shot.Player) is { IsPlayer: true, Appearance: not null } lead && lead.Id == shot.Character
        && lead.Body is not { Dead: true };

    private static void Prepare(object __instance)
    {
        if (s_player.GetValue(__instance) is not { } player || !Players.TryGetValue(player, out var shot)) return;
        shot.Drawn = false;
        if (!Valid(shot) || shot.Error is not null || s_characters.GetValue(__instance) is not { } renderer) return;
        // This modal never opens the character editor. Do not overwrite another stage owner.
        if (s_stage.GetValue(renderer) is EditorStageRequest other && other != shot.Request) { Fail(shot, "Close the character editor before using Photo."); return; }
        shot.Renderer = renderer;
        shot.Request = shot.CreateRequest((long)s_key.Invoke(__instance, [shot.Character])!);
        s_stage.SetValue(renderer, shot.Request);
        Renderers.Remove(renderer); Renderers.Add(renderer, shot);
    }

    private static void KeepPosed(object __instance)
    {
        if (s_player.GetValue(__instance) is { } player && Players.TryGetValue(player, out var shot) && Valid(shot)
            && shot.Renderer is not null) ((HashSet<long>)s_keys.GetValue(__instance)!).Add(shot.Request.Key);
    }

    private static void Draw(object __instance)
    {
        if (!Renderers.TryGetValue(__instance, out var shot) || !Valid(shot) || shot.Error is not null
            || s_stage.GetValue(__instance) is not EditorStageRequest request || request != shot.Request) return;
        object? background = s_background?.GetValue(null);
        try
        {
            s_background?.SetValue(null, -1f);
            s_draw.Invoke(__instance, null); s_resolve.Invoke(__instance, null);
            if (s_view.GetValue(__instance) is null) { shot.Ready = false; return; }
            if (s_texture.GetValue(__instance) is ValueTuple<GpuTexture, SampledTexture> texture)
            {
                s_setTexture.Invoke(shot.Player, [PhotoLayout.Texture, texture.Item1, texture.Item2]);
                shot.Ready = shot.Drawn = true;
            }
        }
        catch (Exception e) { Fail(shot, e.GetBaseException().Message); }
        finally { if (background is not null) s_background!.SetValue(null, background); }
    }

    private static void Camera(object __instance, in EditorStageRequest r, ref (Vector3 Eye, Vector3 Pivot) __result)
    {
        if (!Renderers.TryGetValue(__instance, out var shot) || !Valid(shot) || r != shot.Request) return;
        Vector3? head = shot.Head ? s_bone.Invoke(__instance, [r.Key, "Bip01 Head"]) as Vector3? : null;
        __result = FrameCamera(__result, head, shot.Head, shot.Pan);
    }

    // Translate eye and pivot together: keep the native orbit angle, distance and the character's live pose.
    internal static (Vector3 Eye, Vector3 Pivot) FrameCamera((Vector3 Eye, Vector3 Pivot) camera, Vector3? head, bool focusHead, Vector2 pan)
    {
        Vector3 direction = camera.Eye - camera.Pivot;
        if (direction.LengthSquared() < 1e-6f) return camera;
        Vector3 towardEye = Vector3.Normalize(direction);
        Vector3 up = MathF.Abs(Vector3.Dot(towardEye, Vector3.UnitY)) > .98f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, towardEye));
        up = Vector3.Normalize(Vector3.Cross(towardEye, right));
        Vector3 offset = right * pan.X + up * pan.Y;
        if (focusHead && head is { } at) offset += at - camera.Pivot;
        return (camera.Eye + offset, camera.Pivot + offset);
    }

    private static void EndFrame(object __instance)
    {
        if (s_loopHost.GetValue(__instance) is not { } host || s_player.GetValue(host) is not { } player
            || !Players.TryGetValue(player, out var shot) || shot.Capture is not { } completion) return;
        shot.Capture = null;
        try
        {
            if (!Valid(shot) || !shot.Drawn || shot.Renderer is not { } renderer
                || s_texture.GetValue(renderer) is not ValueTuple<GpuTexture, SampledTexture> texture)
                throw new InvalidOperationException("The selected character is no longer ready for the picture.");
            if (s_gpu.GetValue(renderer) is not GpuContext gpu)
                throw new NotSupportedException("This Photo prototype needs Meitou's Vulkan renderer on Linux or Windows.");
            byte[] pixels = FramebufferCapture.Read(gpu, texture.Item1, PortraitFiles.Size, PortraitFiles.Size);
            completion.TrySetResult(PhotoImage.FromReadback(pixels, PortraitFiles.Size));
        }
        catch (Exception e) { completion.TrySetException(e.GetBaseException()); }
    }

    private static void Fail(Shot shot, string error)
    {
        shot.Error = "Photo preview unavailable. See log."; shot.Ready = false;
        shot.Capture?.TrySetException(new InvalidOperationException(error)); shot.Capture = null;
        s_log("Photo prototype: " + error);
    }
}
