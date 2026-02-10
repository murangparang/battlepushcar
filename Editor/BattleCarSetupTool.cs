// ============================================================
// BattleCarSetupTool.cs - 에디터 자동 셋업 도구
// Unity 메뉴에서 한 번의 클릭으로 전체 테스트 환경 구성
// ============================================================

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using BattleCarSumo.Data;
using BattleCarSumo.Test;

namespace BattleCarSumo.Editor
{
    /// <summary>
    /// Battle Car Sumo 에디터 셋업 도구.
    /// 메뉴: BattleCarSumo → 각 항목
    /// </summary>
    public static class BattleCarSetupTool
    {
        private const string PREFAB_PATH = "Assets/Prefabs";
        private const string SO_PATH = "Assets/ScriptableObjects";
        private const string RESOURCES_PATH = "Assets/Resources";

        #region Menu Items

        [MenuItem("BattleCarSumo/1. 전체 셋업 (All-in-One) ★", false, 0)]
        public static void SetupAll()
        {
            Debug.Log("========== Battle Car Sumo 전체 셋업 시작 ==========");

            EnsureDirectories();
            GameConfig config = CreateGameConfig();
            GameObject vehiclePrefab = CreateVehiclePrefab();
            CreateOfflineTestScene(config, vehiclePrefab);

            Debug.Log("========== 전체 셋업 완료! Play 버튼을 눌러 테스트하세요 ==========");
            Debug.Log("조작법: WASD 이동 | Q=Punch | E=Lift | R=Boost | Space=리셋 | Tab=체급변경 | F1=AI토글");
        }

        [MenuItem("BattleCarSumo/2. ScriptableObject만 생성", false, 20)]
        public static void CreateScriptableObjectsOnly()
        {
            EnsureDirectories();
            CreateGameConfig();
            Debug.Log("ScriptableObject 에셋 생성 완료!");
        }

        [MenuItem("BattleCarSumo/3. 차량 프리팹만 생성", false, 21)]
        public static void CreateVehiclePrefabOnly()
        {
            EnsureDirectories();
            CreateVehiclePrefab();
            Debug.Log("차량 프리팹 생성 완료!");
        }

        [MenuItem("BattleCarSumo/4. 오프라인 테스트 씬 생성", false, 22)]
        public static void CreateTestSceneOnly()
        {
            EnsureDirectories();
            GameConfig config = GetOrCreateGameConfig();
            GameObject vehiclePrefab = GetOrCreateVehiclePrefab();
            CreateOfflineTestScene(config, vehiclePrefab);
        }

        #endregion

        #region Directory Setup

        private static void EnsureDirectories()
        {
            string[] dirs = new string[]
            {
                "Assets/Prefabs",
                "Assets/Prefabs/Vehicle",
                "Assets/Prefabs/Parts",
                "Assets/ScriptableObjects",
                "Assets/ScriptableObjects/Config",
                "Assets/ScriptableObjects/Parts",
                "Assets/Resources",
                "Assets/Scenes",
                "Assets/Materials"
            };

            foreach (string dir in dirs)
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = System.IO.Path.GetDirectoryName(dir).Replace("\\", "/");
                    string folder = System.IO.Path.GetFileName(dir);
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }
        }

        #endregion

        #region GameConfig Creation

        private static GameConfig GetOrCreateGameConfig()
        {
            GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>($"{RESOURCES_PATH}/GameConfig.asset");
            if (config != null) return config;
            return CreateGameConfig();
        }

        private static GameConfig CreateGameConfig()
        {
            GameConfig config = ScriptableObject.CreateInstance<GameConfig>();

            // 기본값은 GameConfig.cs에서 이미 설정됨
            string path = $"{RESOURCES_PATH}/GameConfig.asset";
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            Debug.Log($"GameConfig 생성됨: {path}");
            return config;
        }

        #endregion

        #region Vehicle Prefab Creation

        private static GameObject GetOrCreateVehiclePrefab()
        {
            string path = $"{PREFAB_PATH}/Vehicle/BattleCarPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            return CreateVehiclePrefab();
        }

        private static GameObject CreateVehiclePrefab()
        {
            // === 차량 루트 오브젝트 ===
            GameObject vehicle = new GameObject("BattleCar");

            // Rigidbody
            Rigidbody rb = vehicle.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 3f;
            rb.angularDamping = 6f;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // BoxCollider (차체 전체)
            BoxCollider collider = vehicle.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.8f, 0.8f, 2.5f);
            collider.center = new Vector3(0f, 0.4f, 0f);

            // PhysicMaterial (탄성)
            PhysicsMaterial physMat = new PhysicsMaterial("CarPhysics");
            physMat.bounciness = 0.3f;
            physMat.dynamicFriction = 0.6f;
            physMat.staticFriction = 0.8f;
            physMat.frictionCombine = PhysicsMaterialCombine.Average;
            physMat.bounceCombine = PhysicsMaterialCombine.Maximum;

            string physMatPath = "Assets/Materials/CarPhysicsMaterial.asset";
            AssetDatabase.CreateAsset(physMat, physMatPath);
            collider.material = physMat;

            // OfflineVehicleController
            OfflineVehicleController controller = vehicle.AddComponent<OfflineVehicleController>();

            // === Body (메인 큐브 - 차체) ===
            GameObject body = CreateCubeChild("Body", vehicle.transform,
                new Vector3(0f, 0.4f, 0f),
                new Vector3(1.6f, 0.7f, 2.2f),
                CreateMaterial("CarBody_Mat", new Color(0.2f, 0.5f, 0.9f)));

            // 차체 상단 (캐빈)
            GameObject cabin = CreateCubeChild("Cabin", body.transform,
                new Vector3(0f, 0.5f, -0.1f),
                new Vector3(0.7f, 0.4f, 0.8f),
                CreateMaterial("CarCabin_Mat", new Color(0.3f, 0.6f, 1f)));

            // 전면 표시 (방향 식별용)
            GameObject frontIndicator = CreateCubeChild("FrontIndicator", body.transform,
                new Vector3(0f, 0.1f, 1.15f),
                new Vector3(1.4f, 0.15f, 0.15f),
                CreateMaterial("FrontIndicator_Mat", Color.white));

            // === 바퀴 (시각적) ===
            CreateWheel("Wheel_FL", body.transform, new Vector3(-0.85f, -0.2f, 0.7f));
            CreateWheel("Wheel_FR", body.transform, new Vector3(0.85f, -0.2f, 0.7f));
            CreateWheel("Wheel_RL", body.transform, new Vector3(-0.85f, -0.2f, -0.7f));
            CreateWheel("Wheel_RR", body.transform, new Vector3(0.85f, -0.2f, -0.7f));

            // === 부품 장착점 ===
            // Front Attach Point
            GameObject frontAttach = new GameObject("AttachPoint_Front");
            frontAttach.transform.SetParent(vehicle.transform);
            frontAttach.transform.localPosition = new Vector3(0f, 0.5f, 1.4f);

            // Front Part Visual (Punch Bumper 큐브)
            GameObject frontPart = CreateCubeChild("FrontPart_Punch", frontAttach.transform,
                Vector3.zero,
                new Vector3(1.5f, 0.4f, 0.4f),
                CreateMaterial("FrontPart_Mat", new Color(0.9f, 0.2f, 0.2f)));

            // Rooftop Attach Point
            GameObject rooftopAttach = new GameObject("AttachPoint_Rooftop");
            rooftopAttach.transform.SetParent(vehicle.transform);
            rooftopAttach.transform.localPosition = new Vector3(0f, 1.0f, 0f);

            // Rooftop Part Visual (Lift 큐브)
            GameObject rooftopPart = CreateCubeChild("RooftopPart_Lift", rooftopAttach.transform,
                Vector3.zero,
                new Vector3(0.6f, 0.3f, 0.6f),
                CreateMaterial("RooftopPart_Mat", new Color(0.2f, 0.8f, 0.9f)));

            // Rear Attach Point
            GameObject rearAttach = new GameObject("AttachPoint_Rear");
            rearAttach.transform.SetParent(vehicle.transform);
            rearAttach.transform.localPosition = new Vector3(0f, 0.5f, -1.4f);

            // Rear Part Visual (Boost 큐브)
            GameObject rearPart = CreateCubeChild("RearPart_Boost", rearAttach.transform,
                Vector3.zero,
                new Vector3(0.8f, 0.3f, 0.5f),
                CreateMaterial("RearPart_Mat", new Color(0.2f, 0.9f, 0.3f)));

            // === Controller에 참조 연결 ===
            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("_frontAttachPoint").objectReferenceValue = frontAttach.transform;
            so.FindProperty("_rooftopAttachPoint").objectReferenceValue = rooftopAttach.transform;
            so.FindProperty("_rearAttachPoint").objectReferenceValue = rearAttach.transform;

            GameConfig config = GetOrCreateGameConfig();
            so.FindProperty("_gameConfig").objectReferenceValue = config;
            so.ApplyModifiedProperties();

            // === 프리팹으로 저장 ===
            string prefabPath = $"{PREFAB_PATH}/Vehicle/BattleCarPrefab.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(vehicle, prefabPath);
            Object.DestroyImmediate(vehicle);

            Debug.Log($"차량 프리팹 생성됨: {prefabPath}");
            return prefab;
        }

        #endregion

        #region Offline Test Scene

        private static void CreateOfflineTestScene(GameConfig config, GameObject vehiclePrefab)
        {
            // 새 씬 생성
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // === Directional Light 강화 ===
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    light.intensity = 1.2f;
                    light.color = new Color(1f, 0.97f, 0.9f);
                    light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                    light.shadows = LightShadows.Soft;
                }
            }

            // === 지면 (바닥) ===
            float arenaRadius = config != null ? config.arenaRadius : 15f;
            float arenaHeight = 1f; // 아레나가 지면 위 1m

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f); // 표면이 Y=0
            ground.transform.localScale = new Vector3(arenaRadius * 6f, 1f, arenaRadius * 6f);
            ground.GetComponent<Renderer>().material = CreateMaterial("Ground_Mat", new Color(0.35f, 0.55f, 0.3f));

            // 지면 가장자리 장식 (어두운 테두리)
            GameObject groundBorder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundBorder.name = "GroundBorder";
            groundBorder.transform.position = new Vector3(0f, -0.49f, 0f);
            groundBorder.transform.localScale = new Vector3(arenaRadius * 6f + 2f, 1f, arenaRadius * 6f + 2f);
            groundBorder.GetComponent<Renderer>().material = CreateMaterial("GroundBorder_Mat", new Color(0.25f, 0.4f, 0.2f));

            // === 아레나 (원형 플랫폼, 지면 위 1m) ===
            GameObject arena = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arena.name = "Arena";
            arena.transform.position = new Vector3(0f, arenaHeight, 0f); // Y=1m 위치
            arena.transform.localScale = new Vector3(arenaRadius * 2f, 0.5f, arenaRadius * 2f);

            // ★ CapsuleCollider → MeshCollider 교체 (CapsuleCollider는 돔 모양이라 차량 미끄러짐!)
            Object.DestroyImmediate(arena.GetComponent<Collider>());
            MeshCollider arenaMeshCol = arena.AddComponent<MeshCollider>();
            arenaMeshCol.sharedMesh = arena.GetComponent<MeshFilter>().sharedMesh;

            // 아레나 머테리얼
            Renderer arenaRenderer = arena.GetComponent<Renderer>();
            Material arenaMat = CreateMaterial("Arena_Mat", new Color(0.7f, 0.7f, 0.75f));
            arenaRenderer.material = arenaMat;

            // 아레나 기둥 (아레나를 받치는 원기둥)
            GameObject arenaPillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arenaPillar.name = "ArenaPillar";
            arenaPillar.transform.position = new Vector3(0f, arenaHeight * 0.5f, 0f);
            arenaPillar.transform.localScale = new Vector3(arenaRadius * 1.6f, arenaHeight, arenaRadius * 1.6f);
            arenaPillar.GetComponent<Renderer>().material = CreateMaterial("ArenaPillar_Mat", new Color(0.5f, 0.5f, 0.55f));
            Object.DestroyImmediate(arenaPillar.GetComponent<Collider>()); // 기둥 콜라이더도 제거

            // 아레나 가장자리 표시 (빨간색 링)
            GameObject edgeRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            edgeRing.name = "ArenaEdge";
            edgeRing.transform.position = new Vector3(0f, arenaHeight + 0.26f, 0f);
            edgeRing.transform.localScale = new Vector3(arenaRadius * 2f + 0.5f, 0.01f, arenaRadius * 2f + 0.5f);
            edgeRing.GetComponent<Renderer>().material = CreateMaterial("ArenaEdge_Mat", new Color(0.8f, 0.2f, 0.1f));
            Object.DestroyImmediate(edgeRing.GetComponent<Collider>());

            // 센터 마크
            GameObject centerMark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            centerMark.name = "CenterMark";
            centerMark.transform.position = new Vector3(0f, arenaHeight + 0.26f, 0f);
            centerMark.transform.localScale = new Vector3(3f, 0.01f, 3f);
            centerMark.GetComponent<Renderer>().material = CreateMaterial("CenterMark_Mat", new Color(1f, 1f, 0.3f, 0.7f));
            Object.DestroyImmediate(centerMark.GetComponent<Collider>());

            // === 아레나 벽 (시각적 경계) ===
            CreateArenaBoundaryPosts(arenaRadius, arenaHeight);

            // === Player 1 차량 ===
            float spawnDist = config != null ? config.spawnDistanceFromCenter : 8f;

            GameObject player1 = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab);
            player1.name = "Player1_BattleCar";
            player1.transform.position = new Vector3(spawnDist, arenaHeight + 1.5f, 0f);
            player1.transform.rotation = Quaternion.LookRotation(Vector3.left);

            // Player 1 색상: 파란색
            SetVehicleColor(player1, new Color(0.2f, 0.4f, 0.9f));

            OfflineVehicleController p1Controller = player1.GetComponent<OfflineVehicleController>();
            SerializedObject p1SO = new SerializedObject(p1Controller);
            p1SO.FindProperty("_isPlayer1").boolValue = true;
            p1SO.FindProperty("_gameConfig").objectReferenceValue = config;
            p1SO.ApplyModifiedProperties();

            // === Player 2 차량 (AI 또는 로컬 2P) ===
            GameObject player2 = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab);
            player2.name = "Player2_BattleCar";
            player2.transform.position = new Vector3(-spawnDist, arenaHeight + 1.5f, 0f);
            player2.transform.rotation = Quaternion.LookRotation(Vector3.right);

            // Player 2 색상: 빨간색
            SetVehicleColor(player2, new Color(0.9f, 0.2f, 0.2f));

            OfflineVehicleController p2Controller = player2.GetComponent<OfflineVehicleController>();
            SerializedObject p2SO = new SerializedObject(p2Controller);
            p2SO.FindProperty("_isPlayer1").boolValue = false;
            p2SO.FindProperty("_gameConfig").objectReferenceValue = config;
            p2SO.ApplyModifiedProperties();

            // === Game Manager ===
            GameObject gameManager = new GameObject("OfflineTestManager");
            OfflineTestManager testMgr = gameManager.AddComponent<OfflineTestManager>();

            SerializedObject mgrSO = new SerializedObject(testMgr);
            mgrSO.FindProperty("_gameConfig").objectReferenceValue = config;
            mgrSO.FindProperty("_player1").objectReferenceValue = p1Controller;
            mgrSO.FindProperty("_player2").objectReferenceValue = p2Controller;
            mgrSO.FindProperty("_arenaCenter").objectReferenceValue = arena.transform;
            mgrSO.FindProperty("_enableAI").boolValue = true;
            mgrSO.ApplyModifiedProperties();

            // === 카메라 설정 ===
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = new Vector3(0f, 30f, -20f);
                mainCam.transform.LookAt(Vector3.zero);
                mainCam.fieldOfView = 45f;
                mainCam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);

                // 카메라 추적 스크립트
                OfflineTestCamera camController = mainCam.gameObject.AddComponent<OfflineTestCamera>();
                SerializedObject camSO = new SerializedObject(camController);
                camSO.FindProperty("_target1").objectReferenceValue = player1.transform;
                camSO.FindProperty("_target2").objectReferenceValue = player2.transform;
                camSO.ApplyModifiedProperties();
            }

            // === 아레나 주변 경사면 (자연스러운 낙하 유도) ===
            // 아레나 밖으로 떨어진 차는 지면에 착지하게 됨
            // 지면은 Y=0, 아레나 표면은 Y≈1.25 (arenaHeight + cylinder half height)

            // === 씬 저장 ===
            string scenePath = "Assets/Scenes/OfflineTestScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"오프라인 테스트 씬 생성됨: {scenePath}");
            Debug.Log("Play 버튼을 누르면 바로 테스트할 수 있습니다!");
        }

        private static void CreateArenaBoundaryPosts(float radius, float arenaHeight)
        {
            int postCount = 16;
            float angleStep = 360f / postCount;

            GameObject postsParent = new GameObject("BoundaryPosts");

            for (int i = 0; i < postCount; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(
                    Mathf.Cos(angle) * (radius + 0.5f),
                    arenaHeight + 0.75f,
                    Mathf.Sin(angle) * (radius + 0.5f));

                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"Post_{i:D2}";
                post.transform.SetParent(postsParent.transform);
                post.transform.position = pos;
                post.transform.localScale = new Vector3(0.3f, 1.5f, 0.3f);

                // 포스트 색상: 경고 빨간색 + 흰색 교대
                Color postColor = i % 2 == 0 ?
                    new Color(0.9f, 0.2f, 0.1f) :
                    new Color(0.95f, 0.95f, 0.95f);

                post.GetComponent<Renderer>().material = CreateMaterial($"Post_{i}_Mat", postColor);

                // 콜라이더 제거 (시각적 용도만)
                Object.DestroyImmediate(post.GetComponent<Collider>());
            }
        }

        #endregion

        #region Helper Methods

        private static GameObject CreateCubeChild(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPos;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;

            // 자식 콜라이더 제거 (루트에만 콜라이더)
            Object.DestroyImmediate(cube.GetComponent<Collider>());

            if (mat != null)
                cube.GetComponent<Renderer>().material = mat;

            return cube;
        }

        private static void CreateWheel(string name, Transform parent, Vector3 localPos)
        {
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent);
            wheel.transform.localPosition = localPos;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.35f, 0.15f, 0.35f);

            Object.DestroyImmediate(wheel.GetComponent<Collider>());

            wheel.GetComponent<Renderer>().material = CreateMaterial($"{name}_Mat", new Color(0.15f, 0.15f, 0.15f));
        }

        private static Material CreateMaterial(string name, Color color)
        {
            // URP Lit Shader 사용 시도
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color;

            // _BaseColor 프로퍼티 (URP용)
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);

            string matPath = $"Assets/Materials/{name}.mat";

            // 이미 존재하면 덮어쓰지 않음
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null)
                return existing;

            AssetDatabase.CreateAsset(mat, matPath);
            return mat;
        }

        private static void SetVehicleColor(GameObject vehicle, Color baseColor)
        {
            Transform body = vehicle.transform.Find("Body");
            if (body != null)
            {
                Renderer bodyRenderer = body.GetComponent<Renderer>();
                if (bodyRenderer != null)
                {
                    Material mat = new Material(bodyRenderer.sharedMaterial);
                    mat.color = baseColor;
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", baseColor);
                    bodyRenderer.material = mat;
                }

                // 캐빈도 밝게
                Transform cabin = body.Find("Cabin");
                if (cabin != null)
                {
                    Renderer cabinRenderer = cabin.GetComponent<Renderer>();
                    if (cabinRenderer != null)
                    {
                        Color lighterColor = Color.Lerp(baseColor, Color.white, 0.3f);
                        Material mat = new Material(cabinRenderer.sharedMaterial);
                        mat.color = lighterColor;
                        if (mat.HasProperty("_BaseColor"))
                            mat.SetColor("_BaseColor", lighterColor);
                        cabinRenderer.material = mat;
                    }
                }
            }
        }

        #endregion
    }
}
#endif
