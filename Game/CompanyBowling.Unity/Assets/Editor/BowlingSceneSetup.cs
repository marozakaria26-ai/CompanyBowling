namespace CompanyBowling.Editor
{
    using UnityEngine;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using System.IO;

    public static class BowlingSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/BowlingScene.unity";
        private const string LaneTag = "Lane";
        private const string BallTag = "BowlingBall";
        private const string PinTag = "BowlingPin";

        [MenuItem("Tools/Company Bowling/Setup Test Scene")]
        public static void SetupTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            
            CreateLighting();
            CreateCamera();
            CreateLane();
            CreateBall();
            CreatePins();
            CreateGameManager();
            CreateNetworkManager();
            CreateUI();
            
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            
            Debug.Log($"[BowlingSceneSetup] Scene created at {ScenePath}");
            EditorUtility.DisplayDialog("Scene Setup Complete", 
                $"BowlingScene created at {ScenePath}\n\nPress Play to test.", "OK");
        }

        private static void CreateLighting()
        {
            // Directional light
            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);

            // Ambient light
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.35f);
        }

        private static void CreateCamera()
        {
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
            camObj.transform.position = new Vector3(0, 5, -3);
            camObj.transform.rotation = Quaternion.Euler(25, 0, 0);
        }

        private static void CreateLane()
        {
            var laneObj = new GameObject("Lane");
            laneObj.tag = LaneTag;
            laneObj.layer = LayerMask.NameToLayer("Default");

            // Lane mesh - simple flat plane
            var meshFilter = laneObj.AddComponent<MeshFilter>();
            var meshRenderer = laneObj.AddComponent<MeshRenderer>();
            var collider = laneObj.AddComponent<MeshCollider>();

            // Create lane mesh procedurally (18.3m long, 1.05m wide)
            var mesh = new Mesh();
            float length = 18.3f;
            float width = 1.05f;
            float thickness = 0.05f;

            Vector3[] vertices = new Vector3[8];
            // Bottom face
            vertices[0] = new Vector3(-width/2, 0, 0);
            vertices[1] = new Vector3(width/2, 0, 0);
            vertices[2] = new Vector3(width/2, 0, length);
            vertices[3] = new Vector3(-width/2, 0, length);
            // Top face
            vertices[4] = new Vector3(-width/2, thickness, 0);
            vertices[5] = new Vector3(width/2, thickness, 0);
            vertices[6] = new Vector3(width/2, thickness, length);
            vertices[7] = new Vector3(-width/2, thickness, length);

            int[] triangles = new int[36]
            {
                // Bottom
                0, 2, 1, 0, 3, 2,
                // Top
                4, 5, 6, 4, 6, 7,
                // Front
                4, 7, 3, 4, 3, 0,
                // Back
                5, 1, 2, 5, 2, 6,
                // Left
                4, 0, 1, 4, 1, 5,
                // Right
                7, 3, 2, 7, 2, 6
            };

            Vector2[] uvs = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                uvs[i] = new Vector2(vertices[i].x / width + 0.5f, vertices[i].z / length);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;

            // Lane material - wood-like
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.6f, 0.45f, 0.3f);
            mat.SetFloat("_Glossiness", 0.3f);
            meshRenderer.material = mat;

            collider.sharedMesh = mesh;
            laneObj.transform.position = new Vector3(0, 0, 0);
        }

        private static void CreateBall()
        {
            var ballObj = new GameObject("BowlingBall");
            ballObj.tag = BallTag;
            ballObj.layer = LayerMask.NameToLayer("Default");

            var sphereCollider = ballObj.AddComponent<SphereCollider>();
            sphereCollider.radius = 0.1085f; // Standard bowling ball radius ~10.85cm

            var rb = ballObj.AddComponent<Rigidbody>();
            rb.mass = 7.0f; // ~7kg bowling ball
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var meshFilter = ballObj.AddComponent<MeshFilter>();
            var meshRenderer = ballObj.AddComponent<MeshRenderer>();

            // Create sphere mesh
            var mesh = new Mesh();
            // Use primitive sphere
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            meshFilter.mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);

            // Ball material
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.1f, 0.1f, 0.5f);
            mat.SetFloat("_Glossiness", 0.8f);
            mat.SetFloat("_Metallic", 0.2f);
            meshRenderer.material = mat;

            ballObj.transform.position = new Vector3(0, 0.3f, -2f); // Start position behind foul line
            ballObj.transform.localScale = Vector3.one * 0.217f; // Diameter ~21.7cm
        }

        private static void CreatePins()
        {
            var pinsParent = new GameObject("Pins");
            pinsParent.transform.position = new Vector3(0, 0, 18.3f - 0.5f); // Near end of lane

            // Pin positions in triangular formation (10 pins)
            Vector3[] pinPositions = new Vector3[10];
            float pinSpacing = 0.3f; // ~30cm between pin centers

            // Row 1 (1 pin)
            pinPositions[0] = new Vector3(0, 0, 0);
            // Row 2 (2 pins)
            pinPositions[1] = new Vector3(-pinSpacing/2, 0, pinSpacing * 0.866f);
            pinPositions[2] = new Vector3(pinSpacing/2, 0, pinSpacing * 0.866f);
            // Row 3 (3 pins)
            pinPositions[3] = new Vector3(-pinSpacing, 0, pinSpacing * 1.732f);
            pinPositions[4] = new Vector3(0, 0, pinSpacing * 1.732f);
            pinPositions[5] = new Vector3(pinSpacing, 0, pinSpacing * 1.732f);
            // Row 4 (4 pins)
            pinPositions[6] = new Vector3(-pinSpacing * 1.5f, 0, pinSpacing * 2.598f);
            pinPositions[7] = new Vector3(-pinSpacing * 0.5f, 0, pinSpacing * 2.598f);
            pinPositions[8] = new Vector3(pinSpacing * 0.5f, 0, pinSpacing * 2.598f);
            pinPositions[9] = new Vector3(pinSpacing * 1.5f, 0, pinSpacing * 2.598f);

            for (int i = 0; i < 10; i++)
            {
                var pinObj = new GameObject($"Pin_{i+1}");
                pinObj.tag = PinTag;
                pinObj.layer = LayerMask.NameToLayer("Default");
                pinObj.transform.parent = pinsParent.transform;
                pinObj.transform.localPosition = pinPositions[i];

                var capsuleCollider = pinObj.AddComponent<CapsuleCollider>();
                capsuleCollider.radius = 0.05f;
                capsuleCollider.height = 0.38f;
                capsuleCollider.center = new Vector3(0, 0.19f, 0);
                capsuleCollider.direction = 1; // Y-axis

                var rb = pinObj.AddComponent<Rigidbody>();
                rb.mass = 1.5f; // ~1.5kg per pin
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.1f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var meshFilter = pinObj.AddComponent<MeshFilter>();
                var meshRenderer = pinObj.AddComponent<MeshRenderer>();

                // Create pin shape (cylinder with tapered top)
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                meshFilter.mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(primitive);

                // Pin material - white with red stripes
                var mat = new Material(Shader.Find("Standard"));
                mat.color = Color.white;
                mat.SetFloat("_Glossiness", 0.5f);
                meshRenderer.material = mat;

                pinObj.transform.localScale = new Vector3(0.1f, 0.38f, 0.1f);
            }
        }

        private static void CreateGameManager()
        {
            var gmObj = new GameObject("GameManager");
            var gameController = gmObj.AddComponent<CompanyBowling.Game.BowlingGameController>();

            // Find references
            var ball = GameObject.FindWithTag(BallTag);
            var pins = GameObject.FindGameObjectsWithTag(PinTag);
            var lane = GameObject.FindWithTag(LaneTag);
            var cam = Camera.main;

            // Set up ball start position
            var startPosObj = new GameObject("BallStartPosition");
            startPosObj.transform.position = new Vector3(0, 0.3f, -2f);
            startPosObj.transform.parent = gmObj.transform;

            // Use reflection to set private fields
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            
            if (ball != null)
                typeof(CompanyBowling.Game.BowlingGameController).GetField("bowlingBall", flags)?.SetValue(gameController, ball.GetComponent<Rigidbody>());
            
            typeof(CompanyBowling.Game.BowlingGameController).GetField("bowlingPins", flags)?.SetValue(gameController, pins);
            typeof(CompanyBowling.Game.BowlingGameController).GetField("ballStartPosition", flags)?.SetValue(gameController, startPosObj.transform);
            typeof(CompanyBowling.Game.BowlingGameController).GetField("laneTransform", flags)?.SetValue(gameController, lane?.transform);
            typeof(CompanyBowling.Game.BowlingGameController).GetField("mainCamera", flags)?.SetValue(gameController, cam?.transform);
        }

        private static void CreateNetworkManager()
        {
            var netObj = new GameObject("NetworkManager");
            var gatewayConn = netObj.AddComponent<CompanyBowling.Network.GatewayConnection>();
            
            // Default to localhost for PC testing
            typeof(CompanyBowling.Network.GatewayConnection).GetField("gatewayHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(gatewayConn, "127.0.0.1");
            typeof(CompanyBowling.Network.GatewayConnection).GetField("gatewayPort", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(gatewayConn, 5050);
        }

        private static void CreateUI()
        {
            // Canvas
            var canvasObj = new GameObject("Canvas");
            canvasObj.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // EventSystem
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Status Panel
            var panelObj = new GameObject("StatusPanel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            var panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 1);
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.pivot = new Vector2(0.5f, 1);
            panelRect.anchoredPosition = new Vector2(0, -10);
            panelRect.sizeDelta = new Vector2(0, 180);

            var panelImage = panelObj.AddComponent<UnityEngine.UI.Image>();
            panelImage.color = new Color(0, 0, 0, 0.7f);

            // Layout group
            var layout = panelObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.UpperLeft;

            // Create status texts
            CreateStatusText(panelObj, "Gateway: Disconnected", "GatewayStatusText");
            CreateStatusText(panelObj, "Phone: Disconnected", "PhoneStatusText");
            CreateStatusText(panelObj, "Controller: Not Ready", "ControllerStatusText");
            CreateStatusText(panelObj, "Sensor Rate: 0 Hz", "SensorRateText");
            CreateStatusText(panelObj, "Last Throw: None", "LastThrowText");

            // Buttons
            CreateButton(panelObj, "Create Session & Show QR", "CreateSessionButton");
            CreateButton(panelObj, "Reset Lane", "ResetButton");

            // QR Code display
            var qrObj = new GameObject("QRCodeDisplay");
            qrObj.transform.SetParent(canvasObj.transform, false);
            var qrRect = qrObj.AddComponent<RectTransform>();
            qrRect.anchorMin = new Vector2(1, 0);
            qrRect.anchorMax = new Vector2(1, 0);
            qrRect.pivot = new Vector2(1, 0);
            qrRect.anchoredPosition = new Vector2(-20, 20);
            qrRect.sizeDelta = new Vector2(200, 200);

            var qrImage = qrObj.AddComponent<UnityEngine.UI.RawImage>();
            qrImage.color = Color.white;

            // GameUIController
            var uiController = canvasObj.AddComponent<CompanyBowling.UI.GameUIController>();
            
            // Set references via reflection
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(CompanyBowling.UI.GameUIController).GetField("gatewayStatusText", flags)?.SetValue(uiController, panelObj.transform.Find("GatewayStatusText")?.GetComponent<UnityEngine.UI.Text>());
            typeof(CompanyBowling.UI.GameUIController).GetField("phoneStatusText", flags)?.SetValue(uiController, panelObj.transform.Find("PhoneStatusText")?.GetComponent<UnityEngine.UI.Text>());
            typeof(CompanyBowling.UI.GameUIController).GetField("controllerStatusText", flags)?.SetValue(uiController, panelObj.transform.Find("ControllerStatusText")?.GetComponent<UnityEngine.UI.Text>());
            typeof(CompanyBowling.UI.GameUIController).GetField("sensorRateText", flags)?.SetValue(uiController, panelObj.transform.Find("SensorRateText")?.GetComponent<UnityEngine.UI.Text>());
            typeof(CompanyBowling.UI.GameUIController).GetField("lastThrowText", flags)?.SetValue(uiController, panelObj.transform.Find("LastThrowText")?.GetComponent<UnityEngine.UI.Text>());
            typeof(CompanyBowling.UI.GameUIController).GetField("resetButton", flags)?.SetValue(uiController, panelObj.transform.Find("ResetButton")?.GetComponent<UnityEngine.UI.Button>());
            typeof(CompanyBowling.UI.GameUIController).GetField("createSessionButton", flags)?.SetValue(uiController, panelObj.transform.Find("CreateSessionButton")?.GetComponent<UnityEngine.UI.Button>());
            typeof(CompanyBowling.UI.GameUIController).GetField("qrCodeImage", flags)?.SetValue(uiController, qrImage);
        }

        private static void CreateStatusText(GameObject parent, string text, string name)
        {
            var txtObj = new GameObject(name);
            txtObj.transform.SetParent(parent.transform, false);
            var txt = txtObj.AddComponent<UnityEngine.UI.Text>();
            txt.text = text;
            txt.fontSize = 14;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            
            var layoutElement = txtObj.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.preferredHeight = 25;
        }

        private static void CreateButton(GameObject parent, string text, string name)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent.transform, false);
            var btn = btnObj.AddComponent<UnityEngine.UI.Button>();
            var btnImage = btnObj.AddComponent<UnityEngine.UI.Image>();
            btnImage.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            
            var txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            var txt = txtObj.AddComponent<UnityEngine.UI.Text>();
            txt.text = text;
            txt.fontSize = 14;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            
            var rect = txtObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layoutElement = btnObj.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.preferredHeight = 35;
        }
    }
}