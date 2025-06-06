﻿using System;
using System.Diagnostics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Math;
using ImGuiNET;
using ImGuizmoNET;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager;

namespace SimpleHeels;

public unsafe class GizmoOverlayForMinion {
    private static Matrix4x4 _itemMatrix = Matrix4x4.Identity + Matrix4x4.Zero;
    private static Vector3 _position;
    private static System.Numerics.Vector3 _rotation;
    private static Vector3 _scale = new(1);
    private static readonly Stopwatch UnlockDelay = Stopwatch.StartNew();

    private static OPERATION? LockOperation {
        get;
        set {
            if (value == null) {
                if (UnlockDelay.ElapsedMilliseconds < 250) {
                    return;
                }

                field = null;
            } else {
                field = value;
                UnlockDelay.Restart();
            }
        }
    }

    private static Vector2 _rotateMouseStartPos = new(0);
    private static Vector2 _rotateCenterPos = new(0);
    
    private static bool DrawDirection(ref Matrix4x4 view, ref Matrix4x4 proj, MODE mode, OPERATION operation) {
        return DrawDirection(ref view, ref proj, mode, operation, out _);
    }

    private static bool DrawDirection(ref Matrix4x4 view, ref Matrix4x4 proj, MODE mode, OPERATION operation, out Matrix4x4 delta) {
        try {
            ImGuizmo.SetID((int)ImGui.GetID(nameof(SimpleHeels) + $"#Minion_{operation}"));
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

    public static bool Draw(Companion* companion) {
        if (!HotkeyHelper.CheckHotkeyState(Plugin.Config.MinionGizmoHotkey, false)) {
            return false;
        }

        var modified = false;
        if (companion == null || companion->DrawObject == null) return false;
        var activeCamera = CameraManager.Instance()->GetActiveCamera();
        if (activeCamera == null) return false;
        _position = companion->Position;
        _rotation = new System.Numerics.Vector3(0, companion->Rotation * 180f / MathF.PI, 0);
        
        if (!ImGuizmo.IsUsing()) {
            ImGuizmo.RecomposeMatrixFromComponents(ref _position.X, ref _rotation.X, ref _scale.X, ref _itemMatrix.M11);
        }

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
            
            if (LockOperation is null or OPERATION.TRANSLATE && DrawDirection(ref view, ref proj, MODE.LOCAL, OPERATION.TRANSLATE)) {
                LockOperation = OPERATION.TRANSLATE;
                ImGuizmo.DecomposeMatrixToComponents(ref _itemMatrix.M11, ref _position.X, ref _rotation.X, ref _scale.X);
                companion->SetPosition(_position.X, _position.Y, _position.Z);
                modified = true;
            }
            
            if (LockOperation is null or OPERATION.ROTATE_Y && DrawDirection(ref view, ref proj, MODE.LOCAL, OPERATION.ROTATE_Y)) {
                if (LockOperation != OPERATION.ROTATE_Y) {
                    _rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        _rotateCenterPos = c;
                        LockOperation = OPERATION.ROTATE_Y;
                        return false;
                    } else {
                        LockOperation = OPERATION.BOUNDS;
                        return false;
                    }
                }

                var p1 = _rotateCenterPos;
                var p2 = _rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();
                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                if (MathF.Abs(a) > Constants.FloatDelta) {
                    companion->SetRotation(companion->Rotation - a);
                    _rotateMouseStartPos = p3;
                }

                modified = true;
            }
            
            if (Plugin.Config.TempOffsetPitchRoll && LockOperation is null or OPERATION.ROTATE_X && DrawDirection(ref view, ref proj, MODE.LOCAL, OPERATION.ROTATE_X)) {
                if (LockOperation != OPERATION.ROTATE_X) {
                    _rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        _rotateCenterPos = c;
                        LockOperation = OPERATION.ROTATE_X;
                        return false;
                    } else {
                        LockOperation = OPERATION.BOUNDS;
                        return false;
                    }
                }

                var p1 = _rotateCenterPos;
                var p2 = _rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();
                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                if (MathF.Abs(a) > Constants.FloatDelta) {
                    var pitch = companion->Effects.TiltParam1Value;
                    pitch -= a;
                    if (pitch > MathF.Tau) pitch -= MathF.Tau;
                    if (pitch < 0) pitch += MathF.Tau;
                    companion->Effects.TiltParam1Value = pitch;
                    
                    _rotateMouseStartPos = p3;
                }

                modified = true;
            }

            if (Plugin.Config.TempOffsetPitchRoll && LockOperation is null or OPERATION.ROTATE_Z && DrawDirection(ref view, ref proj, MODE.LOCAL, OPERATION.ROTATE_Z)) {
                if (LockOperation != OPERATION.ROTATE_Z) {
                    _rotateMouseStartPos = ImGui.GetMousePos();
                    if (PluginService.GameGui.WorldToScreen(_position, out var c)) {
                        _rotateCenterPos = c;
                        LockOperation = OPERATION.ROTATE_Z;
                        return false;
                    } else {
                        LockOperation = OPERATION.BOUNDS;
                        return false;
                    }
                }

                var p1 = _rotateCenterPos;
                var p2 = _rotateMouseStartPos;
                var p3 = ImGui.GetMousePos();
                var a = MathF.Atan2(p3.Y - p1.Y, p3.X - p1.X) - MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                if (MathF.Abs(a) > Constants.FloatDelta) {
                    var roll = companion->Effects.TiltParam2Value;
                    roll -= a;
                    if (roll > MathF.Tau) roll -= MathF.Tau;
                    if (roll < 0) roll += MathF.Tau;
                    companion->Effects.TiltParam2Value = roll;
                    _rotateMouseStartPos = p3;
                }

                modified = true;
            }
        } finally {
            ImGuizmo.SetID(-1);
        }
        
        if (!ImGuizmo.IsUsing() && modified == false) {
            LockOperation = null;
        }
        
        return modified;
    }
}
