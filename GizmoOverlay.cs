using System;
using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Common.Math;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager;

namespace SimpleHeels;

public static unsafe class UIGizmoOverlay {
    private static Matrix4x4 _itemMatrix = Matrix4x4.Identity + Matrix4x4.Zero;

    private static Vector3 _position;
    private static FFXIVClientStructs.FFXIV.Common.Math.Quaternion _rotationQ;
    private static System.Numerics.Vector3 _rotation;
    private static Vector3 _scale = new(1);
    private static bool gizmoActiveLastFrame;

    public static bool DrawDirection(ref Matrix4x4 view, ref Matrix4x4 proj, ImGuizmoMode mode, ImGuizmoOperation operation) {
        return DrawDirection(ref view, ref proj, mode, operation, out _);
    }

    public static bool DrawDirection(ref Matrix4x4 view, ref Matrix4x4 proj, ImGuizmoMode mode, ImGuizmoOperation operation, out Matrix4x4 delta) {
        try {
            ImGuizmo.SetID((int)ImGui.GetID(nameof(SimpleHeels) + $"#{operation}"));
            var viewport = ImGui.GetMainViewport();
            ImGuizmo.Enable(true);
            ImGuizmo.SetOrthographic(false);
            ImGuizmo.AllowAxisFlip(false);
            ImGuizmo.SetDrawlist(ImGui.GetBackgroundDrawList());
            ImGuizmo.SetRect(viewport.Pos.X, viewport.Pos.Y, viewport.Size.X, viewport.Size.Y);
            delta = new Matrix4x4();
            return ImGuizmo.Manipulate(ref view.M11, ref proj.M11, operation, mode, ref _itemMatrix.M11, ref delta.M11);
        } finally {
            ImGuizmo.SetID(-1);
        }
    }

    private static Stopwatch _unlockDelay = Stopwatch.StartNew();
    private static ImGuizmoOperation? _lockOperation;

    private static ImGuizmoOperation? LockOperation {
        get => _lockOperation;
        set {
            if (value == null) {
                if (_unlockDelay.ElapsedMilliseconds < 250) {
                    return;
                }

                _lockOperation = null;
            } else {
                _lockOperation = value;
                _unlockDelay.Restart();
            }
        }
    }

    private static Vector2 rotateMouseStartPos = new(0);
    private static Vector2 rotateCenterPos = new(0);

    private static Vector3 WorldToLocal(Vector3 worldPoint, Vector3 objectPosition, Quaternion objectRotation) {
        var translatedPoint = worldPoint - objectPosition;
        var inverseRotation = System.Numerics.Quaternion.Inverse(objectRotation);
        var localPoint = Vector3.Transform(translatedPoint, inverseRotation);
        return localPoint;
    }

    public static Vector2 RotatePoint(Vector2 point, Vector2 origin, float angleInRadians) {
        // Translate point to the origin
        Vector2 translatedPoint = point - origin;

        // Perform rotation
        float cosTheta = (float)Math.Cos(angleInRadians);
        float sinTheta = (float)Math.Sin(angleInRadians);

        float rotatedX = translatedPoint.X * cosTheta - translatedPoint.Y * sinTheta;
        float rotatedY = translatedPoint.X * sinTheta + translatedPoint.Y * cosTheta;

        // Translate point back
        Vector2 rotatedPoint = new Vector2(rotatedX, rotatedY) + origin;

        return rotatedPoint;
    }

    public static bool Draw(TempOffset? target, Character* character, bool allowHorizontal, bool allowRotation) {
        if (gizmoActiveLastFrame == false && !HotkeyHelper.CheckHotkeyState(Plugin.Config.TempOffsetGizmoHotkey, false)) {
            return false;
        }

        gizmoActiveLastFrame = false;
        var modified = false;
        if (target == null) return false;
        if (character == null || character->DrawObject == null) return false;

        var activeCamera = CameraManager.Instance()->GetActiveCamera();
        if (activeCamera == null) return false;

        if (character->Mode is CharacterModes.Mounted or CharacterModes.RidingPillion && character->DrawObject->GetObjectType() == ObjectType.CharacterBase) {
            var charaBase = (CharacterBase*)character->DrawObject;

            _position = charaBase->Skeleton->Transform.Position;
            _rotationQ = charaBase->Skeleton->Transform.Rotation;
            _rotation = new System.Numerics.Vector3(0, _rotationQ.EulerAngles.Y, 0);

        } else {
            _position = character->DrawObject->Position;
            _rotationQ = character->DrawObject->Rotation;
            _rotation = new System.Numerics.Vector3(0, _rotationQ.EulerAngles.Y, 0);
        }
        

       
        if (!ImGuizmo.IsUsing()) {
            ImGuizmo.RecomposeMatrixFromComponents(ref _position.X, ref _rotation.X, ref _scale.X, ref _itemMatrix.M11);
        }

        // PluginService.Log.Debug($"{_rotation.Y}");

        try {
            var cam = activeCamera->SceneCamera.RenderCamera;
            var view = activeCamera->SceneCamera.ViewMatrix;
            var proj = cam->ProjectionMatrix * 1;
            var far = cam->FarPlane;
            var near = cam->NearPlane;
            var clip = far / (far - near);
            proj.M43 = -(clip * near);
            proj.M33 = -((far + near) / (far - near));
            view.M44 = 1.0f;

            if (allowHorizontal && LockOperation is null or ImGuizmoOperation.TranslateZ && DrawDirection(ref view, ref proj, ImGuizmoMode.Local, ImGuizmoOperation.TranslateZ)) {
                LockOperation = ImGuizmoOperation.TranslateZ;
                var lp = WorldToLocal(_itemMatrix.Translation, _position, Quaternion.CreateFromYawPitchRoll(character->Rotation + target.R, 0, 0));
                target.Z += lp.Z;
                modified = true;
            }

            if (allowHorizontal && LockOperation is null or ImGuizmoOperation.TranslateX && DrawDirection(ref view, ref proj, ImGuizmoMode.Local, ImGuizmoOperation.TranslateX)) {
                LockOperation = ImGuizmoOperation.TranslateX;
                var lp = WorldToLocal(_itemMatrix.Translation, _position, Quaternion.CreateFromYawPitchRoll(character->Rotation + target.R, 0, 0));
                target.X += lp.X;
                modified = true;
            }

            if (LockOperation is null or ImGuizmoOperation.TranslateY && DrawDirection(ref view, ref proj, ImGuizmoMode.World, ImGuizmoOperation.TranslateY)) {
                LockOperation = ImGuizmoOperation.TranslateY;
                target.Y += _itemMatrix.Translation.Y - _position.Y;
                ImGuizmo.DecomposeMatrixToComponents(ref _itemMatrix.M11, ref _position.X, ref _rotation.X, ref _scale.X);
                modified = true;
            }

            if (allowRotation && LockOperation is null or ImGuizmoOperation.RotateY && DrawDirection(ref view, ref proj, ImGuizmoMode.Local, ImGuizmoOperation.RotateY)) {
                if (LockOperation != ImGuizmoOperation.RotateY) {
                    rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        rotateCenterPos = c;
                        LockOperation = ImGuizmoOperation.RotateY;
                        return false;
                    } else {
                        LockOperation = ImGuizmoOperation.Bounds;
                        return false;
                    }
                }

                var p1 = rotateCenterPos;
                var p2 = rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();

                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);

                if (MathF.Abs(a) > Constants.FloatDelta) {
                    var p4 = RotatePoint(new Vector2(target.X, target.Z), new Vector2(0, 0), -target.R);
                    target.R -= a;

                    if (target.R > MathF.Tau) target.R -= MathF.Tau;
                    if (target.R < 0) target.R += MathF.Tau;

                    var p5 = RotatePoint(p4, new Vector2(0, 0), target.R);

                    target.X = p5.X;
                    target.Z = p5.Y;

                    rotateMouseStartPos = p3;
                }

                modified = true;
            }

            if (allowRotation && Plugin.Config.TempOffsetPitchRoll && LockOperation is null or ImGuizmoOperation.RotateX && DrawDirection(ref view, ref proj, ImGuizmoMode.Local, ImGuizmoOperation.RotateX)) {
                if (LockOperation != ImGuizmoOperation.RotateX) {
                    rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        rotateCenterPos = c;
                        LockOperation = ImGuizmoOperation.RotateX;
                        return false;
                    } else {
                        LockOperation = ImGuizmoOperation.Bounds;
                        return false;
                    }
                }

                var p1 = rotateCenterPos;
                var p2 = rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();

                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);

                if (MathF.Abs(a) > Constants.FloatDelta) {
                    target.Pitch -= a;

                    if (target.Pitch > MathF.Tau) target.Pitch -= MathF.Tau;
                    if (target.Pitch < 0) target.Pitch += MathF.Tau;

                    rotateMouseStartPos = p3;
                }

                modified = true;
            }

            if (allowRotation && Plugin.Config.TempOffsetPitchRoll && LockOperation is null or ImGuizmoOperation.RotateZ && DrawDirection(ref view, ref proj, ImGuizmoMode.Local, ImGuizmoOperation.RotateZ)) {
                if (LockOperation != ImGuizmoOperation.RotateZ) {
                    rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        rotateCenterPos = c;
                        LockOperation = ImGuizmoOperation.RotateZ;
                        return false;
                    } else {
                        LockOperation = ImGuizmoOperation.Bounds;
                        return false;
                    }
                }

                var p1 = rotateCenterPos;
                var p2 = rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();

                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);

                if (MathF.Abs(a) > Constants.FloatDelta) {
                    target.Roll += a;

                    if (target.Roll > MathF.Tau) target.Roll -= MathF.Tau;
                    if (target.Roll < 0) target.Roll += MathF.Tau;

                    rotateMouseStartPos = p3;
                }

                modified = true;
            }
        } finally {
            ImGuizmo.SetID(-1);
        }

        if (!ImGuizmo.IsUsing() && modified == false) {
            LockOperation = null;
        }

        gizmoActiveLastFrame = ImGuizmo.IsUsing();
        return modified;
    }
}
