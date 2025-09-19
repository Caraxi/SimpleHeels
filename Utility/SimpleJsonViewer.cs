using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SimpleHeels.Utility;

    public class SimpleJsonViewer {
        private string? lastString;
        private JObject? deserialized;
        private Dictionary<string, SimpleJsonViewer?> extras = [];
        
        public void Draw(string rootLabel, string json) {
            if (json != lastString) Update(json);
            var id = 0U;
            if (deserialized != null) {
                DrawObject(rootLabel, deserialized, ref id);
            }
        }

        private void DrawObject(string label, JObject jObject, ref uint id) {
            var treeOpen = ImGui.TreeNodeEx($"{label}##Node{id++}", ImGuiTreeNodeFlags.SpanFullWidth);
            ImGui.SameLine();
            ImGui.TextDisabled($"{jObject.ToString(Formatting.None)}");

            using var _ = ImRaii.PushId($"obj_{id}");
            if (treeOpen) {
                foreach (var node in jObject.Properties().Where(p => p.Value is JObject)) {
                    if (node.Value is JObject childObj) {
                        DrawObject(node.Name, childObj, ref id);
                    }
                }
                
                foreach (var node in jObject.Properties().Where(p => p.Value is JArray)) {
                    if (node.Value is JArray childObj) {
                        DrawArray(node.Name, childObj, ref id);
                    }
                }
                
                foreach (var node in jObject.Properties().Where(p => p.Value is not (JObject or JArray))) {


                    if (node.Value.Type == JTokenType.String) {
                        try {
                            var str = node.Value.ToObject<string>();
                            var decomp = Decompress(str);
                            if (decomp.StartsWith('{') && decomp.EndsWith('}')) {

                                if (!extras.TryGetValue(node.Path, out var extra) || extra == null) {
                                    extras[node.Path] = extra = new SimpleJsonViewer();
                                }
                                
                                extra.Draw(node.Name, decomp);
                                continue;
                            }
                        } catch {
                            //
                        }
                    }

                    ImGui.TreeNodeEx($"[{node.Value.Type}] {node.Name}: {node.Value.ToString(Formatting.None)}##Node{id++}", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                    if (ImGui.BeginPopupContextItem($"popup_{id++}")) {
                        if (ImGui.MenuItem($"Copy Value")) {
                            ImGui.SetClipboardText($"{node.Value.ToString(Formatting.None)}");
                        }
                        ImGui.EndPopup();
                    }
                    
                }
                
                ImGui.TreePop();
            }
        }
        
        private void DrawArray(string label, JArray jArray, ref uint id) {

            var treeOpen = ImGui.TreeNodeEx($"{label}##Node{id++}", ImGuiTreeNodeFlags.SpanFullWidth);
            
            ImGui.SameLine();
            ImGui.TextDisabled($"{jArray.ToString(Formatting.None)}");
            
            
            if (treeOpen) {
                var index = 0;
                foreach (var token in jArray.Children()) {
                    if (token is JObject childObj) {
                        DrawObject($"", childObj, ref id);
                    } else if (token is JArray childArray) {
                        DrawArray($"", childArray, ref id);
                    } else {
                        ImGui.TreeNodeEx($"{token.ToString(Formatting.None)}##Node{id++}", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                    }
                    
                    index++;
                }
                
                ImGui.TreePop();
            }
        }

        private void Update(string json) {
            lastString = json;
            deserialized = JsonConvert.DeserializeObject<JObject>(json);
        }

        public static string Decompress(string? compressedString) {
            if(string.IsNullOrEmpty(compressedString)) return string.Empty;
            byte[] decompressedBytes;
            var compressedStream = new MemoryStream(Convert.FromBase64String(compressedString));
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            {
                using (var decompressedStream = new MemoryStream())
                {
                    gzipStream.CopyTo(decompressedStream);
                    decompressedBytes = decompressedStream.ToArray();
                }
            }

            return Encoding.UTF8.GetString(decompressedBytes);
        }
        
    }
