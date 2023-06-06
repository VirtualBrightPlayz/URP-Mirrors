using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(MeshRenderer))]
public class Mirror : MonoBehaviour
{
    public GameObject thisPortal;
    public Camera linked;
    public RTHandle rt;
    public RTHandle rt2;
    public float value;
    private MeshRenderer mr;
    public int maxRendersPerFrame = 0;
    private int renders = 0;
    public float renderScale = 1f;
    public Vector2Int renderSize = Vector2Int.zero;
    private float oldRenderScale = 1f;
    private Vector2Int oldRenderSize = Vector2Int.zero;
    public bool excludeSceneCam = true;

    private void Awake()
    {
        mr = GetComponent<MeshRenderer>();
    }

    private void OnEnable()
    {
        RTHandles.Initialize(Screen.width, Screen.height);
        if (renderSize.x == 0 || renderSize.y == 0)
        {
            rt = RTHandles.Alloc(Vector2.one * renderScale, name: $"{name}_Main");
            rt2 = RTHandles.Alloc(Vector2.one * renderScale, name: $"{name}_Alt");
        }
        else
        {
            rt = RTHandles.Alloc(renderSize.x, renderSize.y, name: $"{name}_Main");
            rt2 = RTHandles.Alloc(renderSize.x, renderSize.y, name: $"{name}_Alt");
        }
        oldRenderScale = renderScale;
        oldRenderSize = renderSize;
        RenderPipelineManager.beginCameraRendering += OnCameraRender;
    }

    // https://forum.unity.com/threads/camera-current-returns-null-when-calling-it-in-onwillrenderobject-with-universalrp.929880/
    private static Plane[] frustrumPlanes = new Plane[6];

    private static bool IsVisible(Camera camera, Bounds bounds)
    {
        GeometryUtility.CalculateFrustumPlanes(camera, frustrumPlanes);
        return GeometryUtility.TestPlanesAABB(frustrumPlanes, bounds);
    }

    private void OnCameraRender(ScriptableRenderContext arg1, Camera arg2)
    {
        if (arg2.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base && arg2.enabled && IsVisible(arg2, mr.bounds))
        {
            OnWillRenderObjectWCam(arg2, true);
        }
    }

    private void LateUpdate()
    {
        if (oldRenderScale != renderScale || oldRenderSize != renderSize)
        {
            OnDisable();
            OnEnable();
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnCameraRender;
        RTHandles.Release(rt);
        rt = null;
        RTHandles.Release(rt2);
        rt2 = null;
    }

    private void OnWillRenderObjectWCam(Camera current, bool render)
    {
        /*
        if (maxRendersPerFrame > 0)
        {
            if (current != linked && current.enabled)
                renders = maxRendersPerFrame;
            if (renders < 0)
            {
                mr.material.SetTexture("_MainTex", rt[maxRendersPerFrame-1]);
                mr.material.SetTexture("_AltTex", rt2[maxRendersPerFrame-1]);
                return;
            }
            renders--;
        }
        else
        */
        {
            if (current == linked)
                return;
            if (!current.enabled && excludeSceneCam)
                return;
        }
        if (current.cameraType == CameraType.SceneView && excludeSceneCam)
            return;
        if (linked && thisPortal)
        {
            if (!render)
                return;

            if (current.stereoEnabled)
            {
                var pos = current.transform.position;
                var rot = current.transform.rotation;
                var pos2 = current.transform.position;
                var rot2 = current.transform.rotation;

                var mat = current.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                var mat3 = current.GetStereoViewMatrix(Camera.StereoscopicEye.Right);

                var mat2 = current.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                var mat4 = current.GetStereoViewMatrix(Camera.StereoscopicEye.Left);

                RenderCam(current, pos, rot, mat, rt, "_MainTex");
                RenderCam(current, pos2, rot2, mat2, rt2, "_AltTex");
            }
            else
            {
                var pos = current.transform.position;
                var rot = current.transform.rotation;
                var mat = current.projectionMatrix;

                RenderCam(current, pos, rot, mat, rt, "_MainTex");
            }
        }
    }

    public void RenderCam(Camera current, Vector3 pos, Quaternion rot, Matrix4x4 mat, RTHandle rt3, string texId)
    {
        if (rt3 == null)
            return;
        linked.stereoTargetEye = StereoTargetEyeMask.None;

        var prevRenderMat = linked.projectionMatrix;
        var prevRenderNear = linked.nearClipPlane;
        var prevPos = linked.transform.position;
        var prevRot = linked.transform.rotation;

        linked.fieldOfView = current.fieldOfView;
        linked.nearClipPlane = current.nearClipPlane;
        linked.farClipPlane = current.farClipPlane;
        linked.projectionMatrix = mat;

        linked.transform.localPosition = Vector3.Reflect(thisPortal.transform.InverseTransformPoint(pos), Vector3.forward);
        linked.transform.localRotation = Quaternion.LookRotation(Vector3.Reflect(thisPortal.transform.InverseTransformDirection(rot * Vector3.forward), Vector3.forward), Vector3.Reflect(thisPortal.transform.InverseTransformDirection(rot * Vector3.up), Vector3.forward));

        Transform clipPlane = thisPortal.transform;
        int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, (clipPlane.position - linked.transform.position)));

        Vector3 camSpacePos = (linked.worldToCameraMatrix).MultiplyPoint(clipPlane.position);
        Vector3 camSpaceNormal = (linked.worldToCameraMatrix).MultiplyVector(clipPlane.forward) * dot;
        float camSpaceDist = -Vector3.Dot(camSpacePos, camSpaceNormal) + value;
        Vector4 clipPlaneCamSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDist);
        var renderMat = linked.CalculateObliqueMatrix(clipPlaneCamSpace);
        linked.projectionMatrix = renderMat;
        linked.nearClipPlane = Vector3.Distance(linked.transform.position, clipPlane.position);

        linked.forceIntoRenderTexture = true;
        var oldrt = linked.targetTexture;
        // linked.targetTexture = rt3;

        // UniversalRenderPipeline.RenderSingleCamera(ctx, linked);
        RenderPipeline.SubmitRenderRequest(linked, new UniversalRenderPipeline.SingleCameraRequest()
        {
            destination = rt3,
        });

        /*
        linked.nearClipPlane = prevRenderNear;
        linked.projectionMatrix = prevRenderMat;
        linked.transform.rotation = prevRot;
        linked.transform.position = prevPos;
        */
        mr.material.SetTexture(texId, rt3);
    }
}
