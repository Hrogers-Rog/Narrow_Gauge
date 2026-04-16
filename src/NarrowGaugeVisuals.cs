using System;
using System.Collections;
using System.Linq;
using Track;
using UnityEngine;

namespace NarrowGaugeMod
{
    public static class NarrowGaugeVisuals
    {
        private const float StandardInside = 1.435f;
        public  const float NarrowInside   = 0.9144f;
        private const float RailInset      = (StandardInside - NarrowInside) / 2f;
        private const float RebuildDelay   = 0.5f;

        public static void ScheduleReplacement(Graph graph)
        {
            var manager = GameObject.FindObjectOfType<NarrowGaugeManager>();
            if (manager == null) { Main.Warn("[Visuals] Manager not found."); return; }
            manager.StartCoroutine(ReplaceAfterDelay(graph));
        }

        static IEnumerator ReplaceAfterDelay(Graph graph)
        {
            yield return new WaitForSeconds(RebuildDelay);

            var narrowSegments = graph.Segments
                .Where(NarrowGaugeManager.IsNarrowGauge)
                .ToList();

            if (narrowSegments.Count == 0) yield break;

            Main.Log($"[Visuals] Processing {narrowSegments.Count} narrow segment(s).");

            foreach (var seg in narrowSegments)
            {
                try { ApplyToSegment(seg); }
                catch (Exception e) { Main.Error($"[Visuals] Exception on {seg.id}: {e.Message}\n{e.StackTrace}"); }
            }
        }

        static void ApplyToSegment(TrackSegment segment)
        {
            string segName     = "seg-"    + segment.id;
            string bumperNameA = "bumper-" + segment.a?.id;
            string bumperNameB = "bumper-" + segment.b?.id;

            GameObject? segObj  = null;
            GameObject? bumperA = null;
            GameObject? bumperB = null;

            // seg-{id} and bumper-{nodeId} are both tagged TrackMeshGenerated
            foreach (var go in GameObject.FindGameObjectsWithTag("TrackMeshGenerated"))
            {
                if (go.name == segName)     { segObj  = go; continue; }
                if (go.name == bumperNameA) { bumperA = go; continue; }
                if (go.name == bumperNameB) { bumperB = go; continue; }
            }

            // Also check if seg-{id} is a child of a "Generated" container
            if (segObj == null)
            {
                foreach (var go in GameObject.FindGameObjectsWithTag("TrackMeshGenerated"))
                {
                    var c = go.transform.Find(segName);
                    if (c != null) { segObj = c.gameObject; break; }
                }
            }

            if (segObj == null)
            {
                Main.Warn($"[Visuals] '{segName}' not found.");
                return;
            }

            var lRail = segObj.transform.Find("L");
            var rRail = segObj.transform.Find("R");

            if (lRail == null || rRail == null)
            {
                Main.Warn($"[Visuals] L/R missing on '{segName}'.");
                return;
            }

            ShiftMeshX(lRail.GetComponent<MeshFilter>(), -RailInset);
            ShiftMeshX(rRail.GetComponent<MeshFilter>(), +RailInset);
            Main.Log($"[Visuals] Segment {segment.id} railed.");

            if (bumperA != null) FixBumper(bumperA);
            else Main.Log($"[Visuals] No bumper for node {segment.a?.id}");

            if (bumperB != null) FixBumper(bumperB);
            else Main.Log($"[Visuals] No bumper for node {segment.b?.id}");
        }

        static void FixBumper(GameObject bumperModel)
        {
            // bumperModel IS the A-frame (named "bumper-{nodeId}"), tagged TrackMeshGenerated
            // Its parent is the "Generated" container which also has "bumper-track" sibling
            var parent = bumperModel.transform.parent;
            if (parent == null) { Main.Warn($"[Visuals] {bumperModel.name} has no parent."); return; }

            // Scale A-frame X so legs span the narrow gauge
            float xScale = NarrowInside / StandardInside;
            var s = bumperModel.transform.localScale;
            if (!Mathf.Approximately(s.x, xScale))
                bumperModel.transform.localScale = new Vector3(xScale, s.y, s.z);

            // Find bumper-track sibling and narrow its rails
            var trackTF = parent.Find("bumper-track");
            if (trackTF == null) { Main.Warn($"[Visuals] bumper-track not found under {parent.name}."); return; }

            var lMF = trackTF.Find("L")?.GetComponent<MeshFilter>();
            var rMF = trackTF.Find("R")?.GetComponent<MeshFilter>();

            if (lMF?.sharedMesh != null && rMF?.sharedMesh != null
                && lMF.sharedMesh.vertices.Length > 0)
            {
                float centre = (lMF.sharedMesh.vertices[0].x + rMF.sharedMesh.vertices[0].x) / 2f;
                Main.Log($"[Visuals] Bumper {bumperModel.name} centre={centre:F3}");
                ScaleMeshXAroundCentre(lMF, centre, xScale);
                ScaleMeshXAroundCentre(rMF, centre, xScale);
            }

            Main.Log($"[Visuals] Fixed bumper {bumperModel.name}.");
        }

        static void ShiftMeshX(MeshFilter mf, float xShift)
        {
            if (mf?.sharedMesh == null) return;
            if (mf.sharedMesh.name.EndsWith("_narrow")) return;
            var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + "_narrow";
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i].x += xShift;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }

        static void ScaleMeshXAroundCentre(MeshFilter mf, float centre, float scale)
        {
            if (mf?.sharedMesh == null) return;
            if (mf.sharedMesh.name.EndsWith("_narrow")) return;
            var mesh = UnityEngine.Object.Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + "_narrow";
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i].x = centre + (verts[i].x - centre) * scale;
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }
    }
}
