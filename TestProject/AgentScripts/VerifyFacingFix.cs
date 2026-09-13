using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEditor;
public static class VerifyFacingFix {
 public static async Task<object> Main() {
 if(!Application.isPlaying) throw new InvalidOperationException("Play required.");
 var f=UnityEngine.Object.FindAnyObjectByType<CRPG.Gameplay.FacingController>();
 var harness=UnityEngine.Object.FindAnyObjectByType<CharacterLabHarness>();
 var nav=f.GetComponent<UnityEngine.AI.NavMeshAgent>();
 var animator=f.GetComponent<CRPG.Presentation.Animation.CharacterPlayableAnimator>();
 var model=f.transform.Find("VisualRoot/Model"); var head=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="Head");
 var bg=Application.runInBackground; Application.runInBackground=true;
 var records=new List<object>();
 try {
 foreach(var direction in new[]{Vector3.right,Vector3.back,Vector3.left,Vector3.forward}) {
 nav.ResetPath(); nav.Warp(new Vector3(0,0.08f,0));
 f.ClearFacing(CRPG.Gameplay.FacingPriority.Interaction);
 f.GetComponent<CRPG.Navigation.NavMeshCharacterMotor>().SetDestination(nav.transform.position+direction*3f);
 await Task.Delay(850);
 var velocity=nav.velocity;
 var dot=Vector3.Dot(Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized,velocity.normalized);
 records.Add(new {direction=direction.ToString(),speed=velocity.magnitude,headDotVelocity=dot,modelYaw=model.localEulerAngles.y});
 if(velocity.magnitude<0.1f || dot<0.95f) throw new Exception("Facing check failed: "+dot+" velocity "+velocity);
 }
 } finally {nav.ResetPath(); Application.runInBackground=bg;}
 return new {passed=true,samples=records};
 }
}
