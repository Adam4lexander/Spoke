using UnityEngine;
using UnityEngine.UI;

namespace Spoke.Examples {

    public class SpokeMemo : SpokeBehaviour {

        [Header("References")]
        [SerializeField] Text countLabel;
        [SerializeField] Text evenOddLabel;

        // Reactive input states
        State<int> count = State.Create(0);
        State<bool> useUpperCase = State.Create(false);

        protected override void Init(EffectBuilder s) {

            // Memo<T> represents *derived* state.
            // It automatically tracks dependencies and recalculates only when they change.
            // Unlike State<T>, you can't Set() it manually — its value is *computed*.
            //
            // Here, `evenOdd` is a derived value that updates whenever `count` changes.
            var evenOdd = s.UseMemo(s => s.D(count) % 2 == 0 ? "Even" : "Odd");

            // This memo reacts to both `evenOdd` and `useUpperCase`
            // and computes the final label string with dynamic casing.
            var labelText = s.UseMemo(s => {
                var raw = s.D(evenOdd);
                return s.D(useUpperCase) ? raw.ToUpper() : raw.ToLower();
            });

            // Display the current count
            s.UseEffect(s => {
                countLabel.text = $"Count: {s.D(count)}";
            });

            // Display the computed even/odd label
            s.UseEffect(s => {
                evenOddLabel.text = s.D(labelText);
            });
        }

        void Update() {
            // Press UpArrow to increment the count
            if (Input.GetKeyDown(KeyCode.UpArrow)) {
                count.Update(c => c + 1);
            }

            // Press DownArrow to decrement
            if (Input.GetKeyDown(KeyCode.DownArrow)) {
                count.Update(c => c - 1);
            }

            // Press Space to toggle casing
            if (Input.GetKeyDown(KeyCode.Space)) {
                useUpperCase.Update(b => !b);
            }
        }
    }
}