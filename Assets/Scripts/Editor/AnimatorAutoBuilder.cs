#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PGIM.Sekiro2D.Editor
{
    public class AnimatorAutoBuilder
    {
        [MenuItem("Tools/Setup Player Animator")]
        public static void SetupPlayerAnimator()
        {
            string controllerPath = "Assets/Animations/Player.controller";
            string animationsFolder = "Assets/Animations";

            if (!Directory.Exists(animationsFolder)) Directory.CreateDirectory(animationsFolder);

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

            AddParameterIfNotExists(controller, "Speed", AnimatorControllerParameterType.Float);
            AddParameterIfNotExists(controller, "IsGrounded", AnimatorControllerParameterType.Bool);
            AddParameterIfNotExists(controller, "yVelocity", AnimatorControllerParameterType.Float);
            AddParameterIfNotExists(controller, "Attack1", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Attack2", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Attack3", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Parry", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Hurt", AnimatorControllerParameterType.Trigger);
            AddParameterIfNotExists(controller, "Death", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine rootMachine = controller.layers[0].stateMachine;

            AnimatorState idleState = FindOrCreateState(rootMachine, "Idle", "idle.anim", animationsFolder);
            AnimatorState runState = FindOrCreateState(rootMachine, "Run", "Run.anim", animationsFolder);
            AnimatorState jumpState = FindOrCreateState(rootMachine, "Jump", "Jump.anim", animationsFolder);
            AnimatorState attack1State = FindOrCreateState(rootMachine, "Attack1", "Attack_1.anim", animationsFolder);
            AnimatorState attack2State = FindOrCreateState(rootMachine, "Attack2", "Attack_2.anim", animationsFolder);
            AnimatorState attack3State = FindOrCreateState(rootMachine, "Attack3", "Attack_3.anim", animationsFolder);
            AnimatorState parryState = FindOrCreateState(rootMachine, "Parry", "Parry.anim", animationsFolder);
            AnimatorState hurtState = FindOrCreateState(rootMachine, "Hurt", "Hurt.anim", animationsFolder);
            AnimatorState deathState = FindOrCreateState(rootMachine, "Death", "Death.anim", animationsFolder);

            if (rootMachine.defaultState == null && idleState != null) rootMachine.defaultState = idleState;

            AddAnyStateTransition(rootMachine, deathState, "Death");
            AddAnyStateTransition(rootMachine, hurtState, "Hurt");
            AddAnyStateTransition(rootMachine, parryState, "Parry");
            AddAnyStateTransition(rootMachine, attack1State, "Attack1");
            AddAnyStateTransition(rootMachine, attack2State, "Attack2");
            AddAnyStateTransition(rootMachine, attack3State, "Attack3");

            if (idleState != null && runState != null)
            {
                EnsureTransition(idleState, runState, "Speed", AnimatorConditionMode.Greater, 0.1f, false, 0.05f);
                EnsureTransition(runState, idleState, "Speed", AnimatorConditionMode.Less, 0.1f, false, 0.05f);
            }

            if (jumpState != null && idleState != null)
            {
                EnsureTransition(jumpState, idleState, "IsGrounded", AnimatorConditionMode.If, 0f, false, 0.05f);
            }

            AddExitTransition(attack1State, idleState);
            AddExitTransition(attack2State, idleState);
            AddExitTransition(attack3State, idleState);
            AddExitTransition(parryState, idleState);
            AddExitTransition(hurtState, idleState);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AnimatorAutoBuilder] Successfully set up Player Animator!");
        }
        private static void AddParameterIfNotExists(AnimatorController c, string n, AnimatorControllerParameterType t)
        {
            foreach (var p in c.parameters) if (p.name == n) return;
            c.AddParameter(n, t);
        }

        private static AnimatorState FindOrCreateState(AnimatorStateMachine m, string sName, string cName, string f)
        {
            foreach (var cs in m.states)
            {
                if (cs.state.name == sName)
                {
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{f}/{cName}");
                    if (clip != null) cs.state.motion = clip;
                    return cs.state;
                }
            }
            AnimatorState ns = m.AddState(sName);
            AnimationClip ap = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{f}/{cName}");
            if (ap != null) ns.motion = ap;
            return ns;
        }

        private static void AddAnyStateTransition(AnimatorStateMachine m, AnimatorState dest, string trig)
        {
            if (dest == null) return;
            foreach (var t in m.anyStateTransitions)
            {
                if (t.destinationState == dest) return;
            }
            AnimatorStateTransition tr = m.AddAnyStateTransition(dest);
            tr.hasExitTime = false;
            tr.hasFixedDuration = true;
            tr.duration = 0.05f;
            tr.AddCondition(AnimatorConditionMode.If, 0, trig);
        }

        private static void EnsureTransition(AnimatorState from, AnimatorState to, string param, AnimatorConditionMode mode, float threshold, bool hasExitTime, float duration)
        {
            if (from == null || to == null) return;
            foreach (var t in from.transitions)
            {
                if (t.destinationState == to) return;
            }
            AnimatorStateTransition tr = from.AddTransition(to);
            tr.hasExitTime = hasExitTime;
            tr.hasFixedDuration = true;
            tr.duration = duration;
            tr.AddCondition(mode, threshold, param);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState returnState)
        {
            if (from == null || returnState == null) return;
            foreach (var t in from.transitions)
            {
                if (t.hasExitTime && t.destinationState == returnState) return;
            }
            AnimatorStateTransition tr = from.AddTransition(returnState);
            tr.hasExitTime = true;
            tr.exitTime = 0.95f;
            tr.hasFixedDuration = true;
            tr.duration = 0.05f;
        }
    }
}
#endif
