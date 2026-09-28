/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


 /* HEY TRENT HERE! this is a list of changes made to this licensed script
 * ==========================================================================================
 * CHANGE LOG — CubismRenderingInterceptController.cs
 * ==========================================================================================
 * Pass 1 (earlier edit)
 * ------------------------------------------------------------------------------------------
 * 1. Added field `public bool runInEditor = true;`
 *    - Added an early return at the top of OnEnable():
 *        if (!Application.isPlaying && !runInEditor) return;
 *    - When checked (default), behaves exactly like the stock Live2D script, which already
 *      runs in the editor through [ExecuteAlways].
 *    - When unchecked, the component does nothing in edit mode.
 *
 * Pass 2 (sorting / edit-mode fixes)
 * ------------------------------------------------------------------------------------------
 * 2. Note added under `runInEditor`   [comment only, no code change]
 *    - Reminder to leave it checked. Unchecking can leave a sprite hidden in the Scene view
 *      because its Sprite Renderer stays switched off.
 *
 * 3. OnEnable() — material selection   [CHANGED]
 *    - Old: read `_renderer.material`, then swapped to `_renderer.sharedMaterial` inside an
 *      `#if UNITY_EDITOR` block when not playing.
 *    - New: one line:
 *        _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
 *    - Why: reading `.material` in edit mode made Unity create and save a copy of the
 *      material ("Sprite-Unlit-Default (Instance)"). Choosing first avoids the copy.
 *    - The `#if UNITY_EDITOR` block was removed; this one line replaces it.
 *
 * 4. OnEnable() — call to RefreshDrawOrder()   [ADDED]
 *    - Placed right after AddInterceptors(this).
 *    - Why: the manager draws interceptors in the order they registered, and Unity doesn't
 *      guarantee which object's OnEnable runs first, so the stacking was random.
 *
 * 5. OnDisable() — re-enable the renderer   [ADDED]
 *        if (_renderer) { _renderer.enabled = true; }
 *    - Why: OnEnable turns the Sprite Renderer off so only Live2D draws it. Nothing turned
 *      it back on, so disabling or removing this component left the sprite invisible.
 *
 * 6. OnValidate()   [NEW METHOD]
 *    - Calls RefreshDrawOrder() when a value changes in the Inspector (only if the
 *      component is active and enabled).
 *    - Why: new Sorting Order values take effect right away without toggling the component.
 *
 * 7. RefreshDrawOrder()   [NEW METHOD, public]
 *    - Moves this interceptor to its sorted spot in CubismRenderingInterceptorsManager's list.
 *    - Order: lowest GroupSortingOrder first, then lowest SortingOrder first.
 *    - Uses only the manager's existing ReorderIndices() method, so no other Live2D file
 *      was changed.
 *    - Public so gameplay code can call it after changing SortingOrder at runtime.
 *
 * 8. DrawsBefore(other)   [NEW METHOD, private]
 *    - Returns true if this interceptor should draw before `other`.
 *    - Compares GroupSortingOrder first, then SortingOrder.
 *    - Ties return false, so equal values keep registration order. Give each object its
 *      own SortingOrder when their relative order matters.
 *
 * Unchanged: TryDraw(), CheckSkipRendering(), the three intercept modes, camera draw
 * tracking, LateUpdate/OnLateUpdate, the interface methods, and all original comments.
 *
 * Scene setup that goes with Pass 2 (Inspector, not code):
 *    - Intercept controller added to BG (Pre Rendering, Sorting Order mode, Group 0).
 *    - Sorting Order: BG = -300, Ground1 = -200, Circle = -100.
 *    - Plain Sprite-Unlit-Default material reassigned on Ground1 and Circle.
 * ==========================================================================================
 */


using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEngine;
using UnityEngine.Rendering;


namespace Live2D.Cubism.Rendering.URP.RenderingInterceptor
{
    /// <summary>
    /// Base class for rendering interceptors.
    /// Place custom rendering logic by inheriting from this class.
    /// </summary>
   [ExecuteAlways]
    public class CubismRenderingInterceptController : MonoBehaviour, ICubismUpdatable, ICubismRenderingInterceptor
    {
        /// <summary>
        /// Timing for invoking.
        /// </summary>
        public enum InvokeTiming
        {
            PreRendering,
            PostRendering,
        }

        /// <summary>
        /// Timing for invoking setting.
        /// </summary>
        [SerializeField]
        public InvokeTiming InvokeTimingSetting;

        /// <summary>
        /// Modes for invoking.
        /// </summary>
        public enum InterceptingMode
        {
            SortingOrder,
            ZDepth,
            Drawable,
        }

        /// <summary>
        /// Mode for invoking setting.
        /// </summary>
        [SerializeField]
        public InterceptingMode Mode;

        /// <summary>
        /// Group sorting order for the interceptor.
        /// </summary>
        [SerializeField]
        public int GroupSortingOrder = 0;

        /// <summary>
        /// Sorting order for the interceptor.
        /// </summary>
        [SerializeField]
        public int SortingOrder = 0;

        /// <summary>
        /// Target art mesh for the interceptor.
        /// </summary>
        [SerializeField]
        protected CubismDrawable _targetArtMesh;

        /// <summary>
        /// If attached <see cref="CubismRenderController"/>, reference.
        /// </summary>
        protected CubismRenderController _renderController;

        /// <summary>
        /// Renderer reference.
        /// </summary>
        protected Renderer _renderer;

        /// <summary>
        /// <see cref="_rederer"/>'s material reference.
        /// </summary>
        protected Material _material;

        /// <summary>
        /// Camera draw status.
        /// </summary>
        private struct CameraDrawStatus
        {
            public Camera Camera;
            public int LastRenderedPassCount;
        }

        /// <summary>
        /// Camera draw status array.
        /// </summary>
        private CameraDrawStatus[] _cameraDrawStatus;

        /// <summary>
        /// Render pass counter.
        /// </summary>
        private int _renderPassCounter = 0;

        /// <summary>
        /// Execution order of the interceptor.
        /// </summary>
        public virtual int ExecutionOrder
        {
            get
            {
                return CubismUpdateExecutionOrder.CubismPhysicsController + 10;
            }
        }

        /// <summary>
        /// Whether this interceptor needs to be updated during editing.
        /// </summary>
        public virtual bool NeedsUpdateOnEditing
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Whether a <see cref="CubismUpdateController"/> is attached to the same GameObject.
        /// </summary>
        public bool HasUpdateController
        {
            get;
            set;
        }

        public bool runInEditor = true;
        // [Pass 2 note] Leave this CHECKED. When checked, the component behaves exactly like the
        //               stock Live2D version (which already runs in the editor via [ExecuteAlways]).
        //               Unchecking it skips OnEnable in edit mode, which can leave a sprite hidden
        //               in the Scene view because its Sprite Renderer stays switched off.

        /// <summary>
        /// Called by Unity when the component is enabled.
        /// </summary>
        public void OnEnable()
        {
            if (!Application.isPlaying && !runInEditor) return;

            _renderController = GetComponent<CubismRenderController>();

            if (!_renderController)
            {
                _renderer = GetComponent<Renderer>();

                _renderer.enabled = false;

                // [Pass 2 change] Pick the material once, based on edit mode vs play mode.
                // WHY: The old code read `_renderer.material` first and only then swapped to
                //      `sharedMaterial` in edit mode. Reading `.material` in edit mode makes Unity
                //      create and assign a *copy* of the material ("Sprite-Unlit-Default (Instance)")
                //      that gets saved into the scene. Choosing up front avoids creating that copy.
                //      (This one line replaces the old `#if UNITY_EDITOR` block — same result, no leak.)
                _material = Application.isPlaying ? _renderer.material : _renderer.sharedMaterial;
            }

            _cameraDrawStatus = Array.Empty<CameraDrawStatus>();

            CubismRenderingInterceptorsManager.GetInstance().AddInterceptors(this);

            // [Pass 2 addition] Put this interceptor in its correct place in the shared list.
            // WHY: The manager calls interceptors in the order they were registered, and Unity does
            //      not guarantee which object's OnEnable runs first. When two objects (e.g. Ground1
            //      and Circle) want to draw at the same moment, their order was effectively random.
            //      Sorting on registration makes it depend only on the numbers you set.
            RefreshDrawOrder();

            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        } // end of function

        /// <summary>
        /// Called by Unity when the component starts.
        /// </summary>
        public void Start()
        {
            HasUpdateController = TryGetComponent<CubismUpdateController>(out _);
        }

        /// <summary>
        /// Called by Unity when the component is disabled.
        /// </summary>
        public void OnDisable()
        {
            CubismRenderingInterceptorsManager.GetInstance().RemoveInterceptors(this);

            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

            // [Pass 2 addition] Hand the sprite back to Unity's normal renderer.
            // WHY: OnEnable switches the Sprite Renderer off so it is only drawn through Live2D.
            //      Nothing switched it back on, so disabling or removing this component left the
            //      sprite permanently invisible (and that "off" state could be saved in the scene).
            if (_renderer)
            {
                _renderer.enabled = true;
            }
        }

        /// <summary>
        /// [Pass 2 addition] Called by Unity when a value is changed in the Inspector.
        /// WHY: Lets you type a new Sorting Order / Group Sorting Order and see the new
        ///      stacking immediately, instead of having to toggle the component off and on.
        /// </summary>
        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                RefreshDrawOrder();
            }
        }

        /// <summary>
        /// [Pass 2 addition] Moves this interceptor to its sorted position in the manager's list,
        /// ordered by GroupSortingOrder, then SortingOrder (lowest first = drawn first).
        /// Public so gameplay code can call it after changing SortingOrder at runtime.
        /// WHY: Single place that owns "where do I sit in the queue" — OnEnable and OnValidate
        ///      both reuse it (DRY). Only uses the manager's existing ReorderIndices API,
        ///      so no Live2D SDK file other than this one needs to change.
        /// </summary>
        public void RefreshDrawOrder()
        {
            var manager = CubismRenderingInterceptorsManager.GetInstance();
            var targetIndex = 0;

            foreach (var interceptor in manager.Interceptors)
            {
                // Skip myself — I'm the one being moved.
                if (ReferenceEquals(interceptor, this))
                {
                    continue;
                }

                // Stop at the first interceptor that should draw after me; I go right before it.
                // Non-controller interceptors (other ICubismRenderingInterceptor types) keep their place.
                if (interceptor is CubismRenderingInterceptController other && DrawsBefore(other))
                {
                    break;
                }

                targetIndex++;
            }

            // Silently does nothing if this interceptor isn't registered.
            manager.ReorderIndices(this, targetIndex);
        }

        /// <summary>
        /// [Pass 2 addition] True if this interceptor should be drawn before <paramref name="other"/>.
        /// Ties return false, so equal values keep registration order — give each object its own
        /// SortingOrder if their relative order matters.
        /// </summary>
        private bool DrawsBefore(CubismRenderingInterceptController other)
        {
            if (GroupSortingOrder != other.GroupSortingOrder)
            {
                return GroupSortingOrder < other.GroupSortingOrder;
            }

            return SortingOrder < other.SortingOrder;
        }

        /// <summary>
        /// Called by Unity at the beginning of context rendering.
        /// </summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            _renderPassCounter++;
        }

        /// <summary>
        /// Called by Unity during LateUpdate.
        /// </summary>
        public void LateUpdate()
        {
            // Skip if an update controller is present.
            if (HasUpdateController)
            {
                return;
            }

            OnLateUpdate();
        }

        /// <summary>
        /// Called during LateUpdate if no <see cref="CubismUpdateController"/> is present.
        /// </summary>
        public virtual void OnLateUpdate()
        {

        }

        /// <summary>
        /// Called before rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        void ICubismRenderingInterceptor.OnPreRenderingForPass(CubismRenderedEventArgs args)
        {
            OnPreRendering(args);
        }

        /// <summary>
        /// Called before rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        protected virtual void OnPreRendering(CubismRenderedEventArgs args)
        {
            // Skip if not invoking pre-rendering.
            if (InvokeTimingSetting != InvokeTiming.PreRendering)
            {
                return;
            }

            TryDraw(args);
        }

        /// <summary>
        /// Called after rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        void ICubismRenderingInterceptor.OnPostRenderingForPass(CubismRenderedEventArgs args)
        {
            OnPostRendering(args);
        }

        /// <summary>
        /// Called after rendering for each pass.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        protected virtual void OnPostRendering(CubismRenderedEventArgs args)
        {
            // Skip if not invoking post-rendering.
            if (InvokeTimingSetting != InvokeTiming.PostRendering)
            {
                return;
            }

            TryDraw(args);
        }

        /// <summary>
        /// Attempts to draw the object based on the interceptor's settings.
        /// </summary>
        /// <param name="args"> Event arguments. </param>
        private void TryDraw(CubismRenderedEventArgs args)
        {
            var currentCamera = args.PassData.CameraData.camera;

            // Find or create camera draw status.
            var statusIndex = Array.FindIndex(_cameraDrawStatus, status => status.Camera == currentCamera);

            // Create a new entry if not found.
            if (statusIndex < 0)
            {
                // Create a new camera draw status entry.
                Array.Resize(ref _cameraDrawStatus, _cameraDrawStatus.Length + 1);
                statusIndex = _cameraDrawStatus.Length - 1;

                // Initialize the new entry.
                _cameraDrawStatus[statusIndex] = new CameraDrawStatus
                {
                    Camera = currentCamera,
                    LastRenderedPassCount = -1,
                };
            }

            // Get the current rendered frame count.
            if (_cameraDrawStatus[statusIndex].LastRenderedPassCount == _renderPassCounter)
            {
                return;
            }

            var doDraw = true;
            switch (Mode)
            {
                case InterceptingMode.SortingOrder:
                    // Decide whether to draw based on sorting order comparisons.
                    if (args.GroupSortingOrder != GroupSortingOrder || args.SortingOrder < SortingOrder)
                    {
                        doDraw = false;
                    }
                    break;
                case InterceptingMode.ZDepth:
                    // Calculate the distance from the camera to the renderer.
                    var directionToRenderer = transform.position - args.CameraPos;
                    var projection = Vector3.Project(directionToRenderer, args.CameraForward);
                    var distance = projection.magnitude;

                    // Determine whether to draw based on distance comparisons.
                    var canDraw = (args.Distance < distance || args.NextDistance > distance) &&
                                !(distance > args.Distance && distance > args.NextDistance &&
                                  distance > args.PreviousDistance);

                    // Decide whether to draw.
                    if (args.PreviousDistance != null
                        && args.NextDistance != null
                        && canDraw)
                    {
                        doDraw = false;
                        break;
                    }

                    if (distance < args.Distance)
                    {
                        doDraw = false;
                    }
                    break;
                case InterceptingMode.Drawable:
                    if (!_targetArtMesh || args.Drawable != _targetArtMesh)
                    {
                        doDraw = false;
                    }

                    break;
                default:
                    // Skip drawing for unknown modes.
                    break;
            }

            if (!doDraw)
            {
                return;
            }

            if (!_renderController)
            {
                // Draw the object.
                args.CommandBuffer.DrawRenderer(_renderer, _material);
            }
            else
            {
                for (var index = 0; index < _renderController.SortedRenderers.Length; index++)
                {
                    var targetRenderer = _renderController.SortedRenderers[index];

                    // Check whether to skip rendering.
                    CheckSkipRendering(targetRenderer);

                    if (targetRenderer.SkipRendering)
                    {
                        continue;
                    }

                    // Draw the object.
                    targetRenderer.DrawObject(args.CommandBuffer, args.PassData);
                }

                // Submit offscreen draws.
                _renderController.SubmitDrawOffscreen(args.CommandBuffer, args.PassData);
            }

            // Mark as drawn.
            _cameraDrawStatus[statusIndex].LastRenderedPassCount = _renderPassCounter;
        }

        /// <summary>
        /// Checks whether to skip rendering for the given renderer.
        /// </summary>
        /// <param name="targetRenderer"> The target renderer. </param>
        private void CheckSkipRendering(CubismRenderer targetRenderer)
        {
            targetRenderer.SkipRendering |= !targetRenderer
                                                    || !targetRenderer.gameObject.activeSelf
                                                    || !targetRenderer.MeshRenderer
                                                    || !targetRenderer.MeshRenderer.enabled;

            switch (targetRenderer.DrawObjectType)
            {
                case CubismModelTypes.DrawObjectType.Drawable:
                    targetRenderer.SkipRendering |= targetRenderer.Opacity <= 0.0f;
                    break;
                case CubismModelTypes.DrawObjectType.Offscreen:
                    targetRenderer.SkipRendering |= targetRenderer.Offscreen.Opacity <= 0.0f;

                    if (!targetRenderer.SkipRendering)
                    {
                        break;
                    }

                    // Skip rendering for all child drawables and offscreens of the offscreen part.
                    var parts = targetRenderer.RenderController.Model.Parts;
                    CubismPart part = null;
                    for (var partIndex = 0; partIndex < parts.Length; partIndex++)
                    {
                        if (parts[partIndex].UnmanagedIndex != targetRenderer.Offscreen.OwnerIndex)
                        {
                            continue;
                        }

                        part = parts[partIndex];
                        break;
                    }

                    if (!part)
                    {
                        break;
                    }

                    // Skip rendering for all child drawables and offscreens of the offscreen part.
                    for (var drawablesIndex = 0; drawablesIndex < part.AllChildDrawables?.Length; drawablesIndex++)
                    {
                        var childDrawable = part.AllChildDrawables[drawablesIndex];

                        for (var rendererIndex = 0; rendererIndex < targetRenderer.RenderController.Renderers.Length; rendererIndex++)
                        {
                            var checkedRenderer = targetRenderer.RenderController.Renderers[rendererIndex];

                            if (checkedRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Drawable
                                || checkedRenderer.Drawable.UnmanagedIndex != childDrawable.UnmanagedIndex)
                            {
                                continue;
                            }

                            checkedRenderer.SkipRendering = true;
                            break;
                        }
                    }

                    for (var offscreensIndex = 0; offscreensIndex < part.AllChildOffscreens?.Length; offscreensIndex++)
                    {
                        var childOffscreen = part.AllChildOffscreens[offscreensIndex];

                        for (var j = 0; j < targetRenderer.RenderController.Renderers.Length; j++)
                        {
                            var checkedRenderer = targetRenderer.RenderController.Renderers[j];

                            if (checkedRenderer.DrawObjectType != CubismModelTypes.DrawObjectType.Offscreen
                                || checkedRenderer.Offscreen.UnmanagedIndex != childOffscreen.UnmanagedIndex)
                            {
                                continue;
                            }

                            checkedRenderer.SkipRendering = true;
                            break;
                        }
                    }
                    break;
                default:
                    targetRenderer.SkipRendering = true;
                    break;
            }
        }
    }
}