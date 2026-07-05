using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Spoke.Examples.BaseDefence {

    public class MeshFX : SpokeBehaviour {

        [Header("References")]
        [SerializeField] GameObject meshRoot;

        [Header("Shatter Config")]
        [SerializeField] float gravity = 9.8f;
        [SerializeField] float blastSpeed = 10f;
        [SerializeField] float shatterTime = 2f;

        [Header("Blink Config")]
        [SerializeField] float blinkTime = 0.15f;

        State<Color> tint = new(Color.white);
        State<Color> flash = new(new Color(1, 1, 1, 0));   // rgb = colour, a = blend amount
        State<bool> isShattered = new(false);

        Trigger<Color> blinkCommand = Trigger.Create<Color>();
        Trigger shatterCommand = Trigger.Create();
        Trigger restoreCommand = Trigger.Create();

        public ISignal<bool> IsShattered => isShattered;
        public void SetTint(Color colour) => tint.Set(colour);
        public void Blink(Color colour) => blinkCommand.Invoke(colour);
        public void Shatter() => shatterCommand.Invoke();
        public void Restore() => restoreCommand.Invoke();

        readonly struct Piece {
            public readonly Renderer Renderer;
            public readonly Vector3 HomePosition;
            public readonly Quaternion HomeRotation;
            public readonly Color BaseColour;

            public Piece(Renderer renderer) {
                Renderer = renderer;
                HomePosition = renderer.transform.localPosition;
                HomeRotation = renderer.transform.localRotation;
                BaseColour = renderer.sharedMaterial.color;
            }
        }

        List<Piece> pieces = new();

        protected override void Init(EffectBuilder s) {
            foreach (var r in meshRoot.GetComponentsInChildren<Renderer>()) {
                pieces.Add(new Piece(r));
            }

            s.Phase(IsEnabled, s => {
                s.Effect(ApplyColour);

                var dock = s.Dock();
                s.Subscribe(blinkCommand, colour => dock.Effect("blink", Blinking(colour)));
                s.Subscribe(shatterCommand, () => dock.Effect("shatter", Shattering));
            }, restoreCommand);
        }

        EffectBlock ApplyColour => s => {
            var block = new MaterialPropertyBlock();
            s.OnCleanup(() => {
                foreach (var piece in pieces) {
                    if (piece.Renderer) piece.Renderer.SetPropertyBlock(null);
                }
            });
            s.Effect(s => {
                var t = s.D(tint);
                var f = s.D(flash);
                var flashRGB = new Color(f.r, f.g, f.b, 1f);
                foreach (var piece in pieces) {
                    if (!piece.Renderer) continue;
                    var c = Color.Lerp(piece.BaseColour * t, flashRGB, f.a);
                    c.a = piece.BaseColour.a;
                    piece.Renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", c);
                    piece.Renderer.SetPropertyBlock(block);
                }
            });
        };

        EffectBlock Blinking(Color colour) => s => {
            IEnumerator onUpdate() {
                var t = 0f;
                while (t < blinkTime) {
                    flash.Set(new Color(colour.r, colour.g, colour.b, 1f - t / blinkTime));
                    t += Time.deltaTime;
                    yield return null;
                }
                flash.Set(new Color(colour.r, colour.g, colour.b, 0f));
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => { 
                StopCoroutine(routine); 
                flash.Set(new Color(1, 1, 1, 0)); 
            });
        };

        EffectBlock Shattering => s => {
            IEnumerator onUpdate() {
                // Blast pieces outward from the combined mesh center so it reads as an explosion.
                var center = Vector3.zero;
                foreach (var p in pieces) center += p.Renderer.bounds.center;
                if (pieces.Count > 0) center /= pieces.Count;

                var velocity = new Vector3[pieces.Count];
                var angular = new Vector3[pieces.Count];
                for (int i = 0; i < pieces.Count; i++) {
                    var outward = pieces[i].Renderer.transform.position - center;
                    if (outward.sqrMagnitude < 0.0001f) outward = Random.onUnitSphere;
                    var dir = (outward.normalized + Vector3.up * 0.5f + Random.onUnitSphere * 0.25f).normalized;
                    velocity[i] = dir * blastSpeed * Random.Range(0.7f, 1.3f);
                    angular[i] = Random.onUnitSphere * Random.Range(180f, 540f);
                }

                var timer = 0f;
                while (timer < shatterTime) {
                    var dt = Time.deltaTime;
                    for (int i = 0; i < pieces.Count; i++) {
                        if (!pieces[i].Renderer) continue;
                        var tr = pieces[i].Renderer.transform;
                        velocity[i] += Vector3.down * gravity * dt;
                        tr.position += velocity[i] * dt;
                        tr.Rotate(angular[i] * dt, Space.World);
                    }
                    timer += dt;
                    yield return null;
                }
                foreach (var p in pieces) {
                    if (p.Renderer) p.Renderer.enabled = false;
                }
                isShattered.Set(true);
            }
            var routine = StartCoroutine(onUpdate());
            s.OnCleanup(() => {
                StopCoroutine(routine);
                isShattered.Set(false);
                foreach (var p in pieces) {
                    if (!p.Renderer) continue;
                    p.Renderer.enabled = true;
                    p.Renderer.transform.localPosition = p.HomePosition;
                    p.Renderer.transform.localRotation = p.HomeRotation;
                }
            });
        };
    }
}
