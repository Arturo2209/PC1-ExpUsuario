using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Genera el escenario "Desierto" completo desde el menú  Desierto > Generar escenario:
/// texturas procedurales, materiales, mallas, prefabs, terreno esculpido y pintado,
/// decoración, iluminación, cielo, niebla, post-procesado, partículas y jugador.
/// </summary>
public static class GeneradorDesierto
{
    const string Raiz = "Assets/Desierto";
    const float Tam = 500f, Alto = 80f;
    const int ResAlt = 513, ResMapa = 512, ResDet = 512, ResTex = 512;
    static readonly Vector3 Centro = new Vector3(250f, 0f, 250f);
    static readonly Vector3 Campamento = new Vector3(262f, 0f, 268f);

    static Random rnd;
    static float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    class Texturas
    {
        public Texture2D arena, arenaN, grieta, grietaN, roca, rocaN, piedras, piedrasN,
            estratos, estratosN, cactus, cactusN, madera, maderaN, hierba, suave;
    }

    class Materiales
    {
        public Material cactus, roca, estratos, madera, metal, hueso, flor, florAmarilla, fruto,
            arbusto, brasa, oscuro, plumas, cabeza, fuego, humo, polvo, cielo, terreno;
    }

    class Prefabs
    {
        public GameObject saguaro, cactusBarril, nopal, mesa, pilar, arbusto, craneo, cartel,
            cerca, fogata, barril, tronco, buitre;
        public GameObject[] rocas;
    }

    // =====================================================================
    //  MENÚ
    // =====================================================================

    [MenuItem("Desierto/Generar escenario", priority = 0)]
    public static void Generar()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        rnd = new Random(2026);
        try
        {
            CrearCarpetas();
            Progreso("Generando texturas...", 0.05f);
            var t = CrearTexturas();
            Progreso("Creando materiales...", 0.35f);
            var m = CrearMateriales(t);

            var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Progreso("Creando mallas y prefabs...", 0.45f);
            var p = CrearPrefabs(m);
            Progreso("Esculpiendo terreno...", 0.6f);
            var terreno = CrearTerreno(t, m);
            Progreso("Iluminación y ambiente...", 0.75f);
            CrearAmbiente(m);
            Progreso("Decorando...", 0.85f);
            Decorar(terreno, p);
            var cam = CrearJugador(terreno, m);

            string ruta = Raiz + "/Escenas/Desierto.unity";
            EditorSceneManager.SaveScene(escena, ruta);
            AgregarABuild(ruta);
            AssetDatabase.SaveAssets();

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.AlignViewToObject(cam.transform);
            Debug.Log("Escenario Desierto generado en " + ruta + ". Pulsa Play para recorrerlo (WASD, Shift, Espacio, clic para mirar).");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void Progreso(string texto, float valor) => EditorUtility.DisplayProgressBar("Generando desierto", texto, valor);

    static void CrearCarpetas()
    {
        foreach (var sub in new[] { "Texturas", "Materiales", "Mallas", "Prefabs", "Terreno", "Ambiente", "Escenas" })
            if (!AssetDatabase.IsValidFolder(Raiz + "/" + sub)) AssetDatabase.CreateFolder(Raiz, sub);
    }

    static void AgregarABuild(string ruta)
    {
        var lista = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (lista.Exists(s => s.path == ruta)) return;
        lista.Insert(0, new EditorBuildSettingsScene(ruta, true));
        EditorBuildSettings.scenes = lista.ToArray();
    }

    // =====================================================================
    //  RUIDO (repetible / "tileable")
    // =====================================================================

    static float Hash(int x, int y, int semilla)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + semilla * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static int Envolver(int v, int p) => ((v % p) + p) % p;

    static float RuidoValor(float x, float y, int px, int py, int semilla)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int ax = Envolver(x0, px), bx = Envolver(x0 + 1, px);
        int ay = Envolver(y0, py), by = Envolver(y0 + 1, py);
        float a = Hash(ax, ay, semilla), b = Hash(bx, ay, semilla);
        float c = Hash(ax, by, semilla), d = Hash(bx, by, semilla);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>Ruido fractal repetible en u,v ∈ [0,1).</summary>
    static float Fbm(float u, float v, int periodo, int octavas, int semilla)
    {
        float suma = 0, amp = 0.5f, norm = 0;
        int p = periodo;
        for (int i = 0; i < octavas; i++)
        {
            suma += amp * RuidoValor(u * p, v * p, p, p, semilla + i * 17);
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return suma / norm;
    }

    /// <summary>Voronoi repetible: distancia al punto más cercano (f1) y al segundo (f2), en unidades de celda.</summary>
    static void Voronoi(float u, float v, int n, int semilla, out float f1, out float f2, out int celda)
    {
        float x = u * n, y = v * n;
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        f1 = f2 = 9f;
        celda = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = Envolver(gx, n), wy = Envolver(gy, n);
                float px = gx + Hash(wx, wy, semilla);
                float py = gy + Hash(wx, wy, semilla + 101);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; celda = wx + wy * n; }
                else if (d < f2) f2 = d;
            }
    }

    static float Suave(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    static Color Opaco(Color c) { c.a = 1f; return c; }

    // =====================================================================
    //  TEXTURAS
    // =====================================================================

    static Texturas CrearTexturas()
    {
        var t = new Texturas();

        GenerarTex("Arena", (u, v) =>
        {
            float n = Fbm(u, v, 4, 5, 1);
            float onda = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * (7 * u + 3 * v) + 5f * Fbm(u, v, 3, 3, 2));
            float grano = Hash((int)(u * ResTex), (int)(v * ResTex), 3);
            float h = 0.55f * n + 0.35f * onda + 0.1f * grano;
            var c = Color.Lerp(C(0.80f, 0.58f, 0.36f), C(0.95f, 0.78f, 0.55f), h) * (0.93f + 0.1f * grano);
            return (Opaco(c), h);
        }, 1.5f, out t.arena, out t.arenaN);

        GenerarTex("TierraAgrietada", (u, v) =>
        {
            Voronoi(u, v, 7, 5, out float f1, out float f2, out _);
            float borde = f2 - f1;
            float grieta = 1f - Suave(0.02f, 0.07f, borde);
            float n = Fbm(u, v, 6, 4, 6);
            var baseC = Color.Lerp(C(0.68f, 0.48f, 0.32f), C(0.84f, 0.64f, 0.44f), n);
            baseC *= 0.9f + 0.12f * Suave(0.05f, 0.25f, borde);
            var c = Color.Lerp(baseC, C(0.28f, 0.18f, 0.11f), grieta);
            return (Opaco(c), (1f - grieta) * (0.7f + 0.3f * n));
        }, 6f, out t.grieta, out t.grietaN);

        GenerarTex("Roca", (u, v) =>
        {
            float n = Fbm(u, v, 4, 6, 7);
            float fino = Fbm(u, v, 16, 3, 8);
            float bandas = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 5 * v + 3f * n);
            var c = Color.Lerp(Color.Lerp(C(0.50f, 0.24f, 0.15f), C(0.70f, 0.38f, 0.22f), n), C(0.80f, 0.52f, 0.34f), bandas * 0.35f);
            c *= 0.9f + 0.2f * fino;
            return (Opaco(c), n * 0.6f + fino * 0.4f);
        }, 4f, out t.roca, out t.rocaN);

        GenerarTex("Pedregoso", (u, v) =>
        {
            Voronoi(u, v, 16, 9, out float f1, out _, out int celda);
            float n = Fbm(u, v, 8, 4, 10);
            float piedra = 1f - Suave(0.28f, 0.36f, f1);
            float tono = Hash(celda, 0, 11);
            var suelo = Color.Lerp(C(0.70f, 0.52f, 0.34f), C(0.82f, 0.64f, 0.44f), n);
            var roca = Color.Lerp(C(0.45f, 0.36f, 0.30f), C(0.72f, 0.46f, 0.32f), tono) * (0.85f + 0.3f * (1f - f1));
            var c = Color.Lerp(suelo, roca, piedra);
            return (Opaco(c), piedra * (1f - f1 / 0.36f) + n * 0.2f);
        }, 4f, out t.piedras, out t.piedrasN);

        var paleta = new[] { C(0.72f, 0.36f, 0.20f), C(0.83f, 0.52f, 0.32f), C(0.58f, 0.27f, 0.16f), C(0.90f, 0.66f, 0.45f) };
        GenerarTex("Estratos", (u, v) =>
        {
            float n = Fbm(u, v, 3, 5, 12);
            float tb = v * 8 + 0.6f * n;
            int idx = Envolver(Mathf.FloorToInt(tb), 8);
            float b = Mathf.Repeat(tb, 1f);
            var colorBanda = paleta[Mathf.Min(3, (int)(Hash(idx, 0, 13) * 4))];
            float borde = Suave(0f, 0.08f, b) * (1f - Suave(0.92f, 1f, b));
            float fino = Fbm(u, v, 16, 3, 14);
            var c = colorBanda * (0.75f + 0.25f * borde) * (0.85f + 0.3f * fino);
            return (Opaco(c), b * 0.3f + borde * 0.4f + fino * 0.3f);
        }, 4f, out t.estratos, out t.estratosN);

        GenerarTex("Cactus", (u, v) =>
        {
            float costilla = 0.5f + 0.5f * Mathf.Cos(2 * Mathf.PI * 16 * u);
            float n = Fbm(u, v, 4, 4, 15);
            var c = Color.Lerp(C(0.16f, 0.30f, 0.13f), C(0.36f, 0.55f, 0.26f), costilla * 0.7f + n * 0.3f);
            // Espinas sobre las crestas
            float cu = u * 16, cv = v * 48;
            int iu = Mathf.RoundToInt(cu), iv = Mathf.FloorToInt(cv);
            float du = (cu - iu) * (ResTex / 16f), dv = (cv - iv - 0.5f) * (ResTex / 48f);
            float espina = (Hash(Envolver(iu, 16), iv, 16) > 0.3f && du * du + dv * dv < 4.8f) ? 1f : 0f;
            c = Color.Lerp(c, C(0.95f, 0.90f, 0.75f), espina);
            return (Opaco(c), costilla * 0.7f + espina * 0.5f);
        }, 3f, out t.cactus, out t.cactusN);

        GenerarTex("Madera", (u, v) =>
        {
            float n = Fbm(u, v, 2, 4, 17);
            float veta = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * (18 * u + 2 * n));
            float raya = RuidoValor(u * 64, v * 4, 64, 4, 18);
            var c = Color.Lerp(C(0.30f, 0.20f, 0.12f), C(0.58f, 0.42f, 0.26f), veta * 0.5f + raya * 0.5f);
            c = Color.Lerp(c, C(0.55f, 0.52f, 0.48f), 0.25f);
            return (Opaco(c), veta * 0.5f + raya * 0.5f);
        }, 3f, out t.madera, out t.maderaN);

        t.hierba = CrearTexHierba();
        t.suave = CrearTexSuave();
        return t;
    }

    static void GenerarTex(string nombre, Func<float, float, (Color c, float h)> f, float relieve, out Texture2D color, out Texture2D normal)
    {
        int s = ResTex;
        var px = new Color[s * s];
        var alt = new float[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                var r = f((x + 0.5f) / s, (y + 0.5f) / s);
                px[y * s + x] = r.c;
                alt[y * s + x] = r.h;
            }
        color = GuardarTex(nombre, px, s, s, false, false, false);

        var nm = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = alt[y * s + (x + 1) % s] - alt[y * s + (x - 1 + s) % s];
                float dy = alt[((y + 1) % s) * s + x] - alt[((y - 1 + s) % s) * s + x];
                var n = new Vector3(-dx * relieve, -dy * relieve, 1f).normalized;
                nm[y * s + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        normal = GuardarTex(nombre + "_N", nm, s, s, true, false, false);
    }

    static Texture2D CrearTexHierba()
    {
        const int s = 256;
        var px = new Color[s * s];
        var local = new Random(7);
        for (int b = 0; b < 34; b++)
        {
            float x0 = 30 + (float)local.NextDouble() * (s - 60);
            float alto = s * (0.45f + 0.5f * (float)local.NextDouble());
            float inclina = ((float)local.NextDouble() - 0.5f) * 90f;
            float grosor = 2f + 2.5f * (float)local.NextDouble();
            var col = Color.Lerp(C(0.78f, 0.64f, 0.40f), C(0.52f, 0.40f, 0.24f), (float)local.NextDouble());
            for (int k = 0; k < alto; k++)
            {
                float t = k / alto;
                float cx = x0 + inclina * t * t;
                float g = grosor * (1f - t);
                var c = Color.Lerp(col * 0.7f, col * 1.1f, t);
                c.a = 1f;
                for (int dx = Mathf.FloorToInt(-g); dx <= Mathf.CeilToInt(g); dx++)
                {
                    int x = Mathf.RoundToInt(cx) + dx;
                    if (x >= 0 && x < s && k < s) px[k * s + x] = c;
                }
            }
        }
        return GuardarTex("HierbaSeca", px, s, s, false, true, true);
    }

    static Texture2D CrearTexSuave()
    {
        const int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                float a = 1f - Suave(0f, 1f, d);
                px[y * s + x] = new Color(1, 1, 1, a * a);
            }
        return GuardarTex("ParticulaSuave", px, s, s, false, true, true);
    }

    static Texture2D GuardarTex(string nombre, Color[] px, int w, int h, bool normal, bool alfa, bool clamp)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        string ruta = $"{Raiz}/Texturas/{nombre}.png";
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !normal;
        imp.alphaIsTransparency = alfa;
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 4;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }

    // =====================================================================
    //  MATERIALES
    // =====================================================================

    static Materiales CrearMateriales(Texturas t)
    {
        var m = new Materiales
        {
            cactus = Mat("Cactus", Color.white, t.cactus, t.cactusN, 0.25f, new Vector2(1, 2)),
            roca = Mat("Roca", Color.white, t.roca, t.rocaN, 0.1f, Vector2.one),
            estratos = Mat("Estratos", Color.white, t.estratos, t.estratosN, 0.05f, Vector2.one),
            madera = Mat("Madera", Color.white, t.madera, t.maderaN, 0.1f, Vector2.one),
            metal = Mat("MetalOxidado", C(0.40f, 0.26f, 0.18f), null, null, 0.35f, Vector2.one, 0.6f),
            hueso = Mat("Hueso", C(0.92f, 0.88f, 0.78f), null, null, 0.3f, Vector2.one),
            flor = Mat("FlorRosa", C(0.95f, 0.30f, 0.45f), null, null, 0.2f, Vector2.one),
            florAmarilla = Mat("FlorAmarilla", C(1f, 0.85f, 0.2f), null, null, 0.2f, Vector2.one),
            fruto = Mat("Tuna", C(0.75f, 0.10f, 0.25f), null, null, 0.4f, Vector2.one),
            arbusto = Mat("ArbustoSeco", C(0.50f, 0.37f, 0.22f), null, null, 0.05f, Vector2.one),
            oscuro = Mat("Oscuro", C(0.08f, 0.07f, 0.07f), null, null, 0.1f, Vector2.one),
            plumas = Mat("Plumas", C(0.16f, 0.12f, 0.10f), null, null, 0.15f, Vector2.one),
            cabeza = Mat("CabezaBuitre", C(0.78f, 0.38f, 0.36f), null, null, 0.2f, Vector2.one),
            brasa = Mat("Brasa", C(0.15f, 0.05f, 0.02f), null, null, 0.1f, Vector2.one),
            fuego = MatParticula("ParticulaFuego", t.suave, true),
            humo = MatParticula("ParticulaHumo", t.suave, false),
            polvo = MatParticula("ParticulaPolvo", t.suave, false),
        };

        m.brasa.EnableKeyword("_EMISSION");
        m.brasa.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.05f) * 3f);
        m.brasa.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        // Cielo procedural con sol grande y atmósfera cálida
        m.cielo = new Material(Shader.Find("Skybox/Procedural"));
        m.cielo.SetFloat("_SunDisk", 2);
        m.cielo.SetFloat("_SunSize", 0.09f);
        m.cielo.SetFloat("_SunSizeConvergence", 3f);
        m.cielo.SetFloat("_AtmosphereThickness", 1.7f);
        m.cielo.SetColor("_SkyTint", C(1f, 0.72f, 0.45f));
        m.cielo.SetColor("_GroundColor", C(0.62f, 0.45f, 0.32f));
        m.cielo.SetFloat("_Exposure", 1.25f);
        AssetDatabase.CreateAsset(m.cielo, Raiz + "/Materiales/Cielo.mat");

        // Material del terreno (URP)
        var baseTer = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat");
        m.terreno = baseTer != null ? new Material(baseTer) : new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
        m.terreno.EnableKeyword("_NORMALMAP");
        AssetDatabase.CreateAsset(m.terreno, Raiz + "/Materiales/Terreno.mat");
        return m;
    }

    static Material Mat(string nombre, Color color, Texture2D tex, Texture2D normal, float brillo, Vector2 tiling, float metal = 0f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        if (tex != null)
        {
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
        }
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Smoothness", brillo);
        m.SetFloat("_Metallic", metal);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, $"{Raiz}/Materiales/{nombre}.mat");
        return m;
    }

    static Material MatParticula(string nombre, Texture2D tex, bool aditivo)
    {
        var src = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
        var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_Blend", aditivo ? 2 : 0);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(aditivo ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, $"{Raiz}/Materiales/{nombre}.mat");
        return m;
    }

    // =====================================================================
    //  MALLAS PROCEDURALES (estilo low-poly)
    // =====================================================================

    /// <summary>Construye una malla con caras planas, orientando cada triángulo hacia "afuera".</summary>
    static Mesh MallaPlana(string nombre, List<Vector3> pos, List<Vector2> uv, List<int> tris, Func<Vector3, Vector3> afuera)
    {
        var v = new List<Vector3>();
        var u = new List<Vector2>();
        var t = new List<int>();
        for (int i = 0; i < tris.Count; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            Vector3 centro = (pos[a] + pos[b] + pos[c]) / 3f;
            if (Vector3.Dot(n, afuera(centro)) < 0) { int x = b; b = c; c = x; }
            foreach (int k in new[] { a, b, c })
            {
                t.Add(v.Count);
                v.Add(pos[k]);
                u.Add(uv[k]);
            }
        }
        var m = new Mesh { name = nombre };
        m.SetVertices(v);
        m.SetUVs(0, u);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateTangents();
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, $"{Raiz}/Mallas/{nombre}.asset");
        return m;
    }

    static float Ruido3(Vector3 p) =>
        (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 57.3f, p.x + 11.1f)) / 3f;

    static Mesh MallaRoca(string nombre, int semilla)
    {
        float g = (1f + Mathf.Sqrt(5f)) / 2f;
        var pos = new List<Vector3>
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        for (int i = 0; i < pos.Count; i++) pos[i] = pos[i].normalized;
        var tris = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        for (int s = 0; s < 2; s++)
        {
            var cache = new Dictionary<long, int>();
            var nuevos = new List<int>();
            int Medio(int a, int b)
            {
                long clave = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(clave, out int idx)) return idx;
                pos.Add(((pos[a] + pos[b]) * 0.5f).normalized);
                cache[clave] = pos.Count - 1;
                return pos.Count - 1;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Medio(a, b), bc = Medio(b, c), ca = Medio(c, a);
                nuevos.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = nuevos;
        }

        var off = new Vector3(semilla * 13.1f, semilla * 7.7f, semilla * 3.3f);
        var estira = new Vector3(1f + 0.15f * (semilla % 3), 0.75f, 1f - 0.1f * (semilla % 2));
        var uv = new List<Vector2>();
        for (int i = 0; i < pos.Count; i++)
        {
            Vector3 n = pos[i];
            float d = 1f + 0.55f * (Ruido3(n * 1.3f + off) - 0.5f) + 0.2f * (Ruido3(n * 3.5f + off) - 0.5f);
            Vector3 p = Vector3.Scale(n * d, estira);
            if (p.y < -0.3f) p.y = -0.3f + (p.y + 0.3f) * 0.2f;
            pos[i] = p;
            uv.Add(new Vector2((p.x + p.z) * 0.5f, p.y * 0.5f));
        }
        return MallaPlana(nombre, pos, uv, tris, c => c);
    }

    /// <summary>Formación rocosa tipo mesa/pilar (butte) con perfil variable y bordes irregulares.</summary>
    static Mesh MallaFormacion(string nombre, float alto, float radio, Func<float, float> perfil, int semilla)
    {
        const int S = 28, A = 12;
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();
        var local = new Random(semilla);
        float[] amp = { 0.12f, 0.08f, 0.05f }, fase = new float[3];
        int[] frec = { 2, 3, 5 };
        for (int i = 0; i < 3; i++) fase[i] = (float)local.NextDouble() * 6.28f;
        float perimetro = 2 * Mathf.PI * radio;
        const float bajo = -3f;

        for (int k = 0; k <= A; k++)
        {
            float t = k / (float)A;
            float y = Mathf.Lerp(bajo, alto, t);
            float jit = 1f + ((float)local.NextDouble() - 0.5f) * 0.08f;
            for (int s = 0; s <= S; s++)
            {
                float ang = (s % S) / (float)S * Mathf.PI * 2f;
                float n = 1f;
                for (int i = 0; i < 3; i++) n += amp[i] * Mathf.Sin(frec[i] * ang + fase[i] + t * 1.5f);
                float r = radio * perfil(t) * n * jit;
                pos.Add(new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r));
                uv.Add(new Vector2(s / (float)S * perimetro / 8f, y / 6f));
            }
        }
        for (int k = 0; k < A; k++)
            for (int s = 0; s < S; s++)
            {
                int a = k * (S + 1) + s, b = a + 1, c = a + S + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        // Tapa superior
        int inicioTapa = pos.Count;
        int centroTapa = pos.Count;
        pos.Add(new Vector3(0, alto + radio * 0.03f, 0));
        uv.Add(Vector2.zero);
        for (int s = 0; s <= S; s++)
        {
            var p = pos[A * (S + 1) + s];
            pos.Add(p);
            uv.Add(new Vector2(p.x / 8f, p.z / 8f));
        }
        for (int s = 0; s < S; s++) tris.AddRange(new[] { centroTapa, inicioTapa + 1 + s, inicioTapa + 2 + s });

        float limiteTapa = alto - 0.01f;
        return MallaPlana(nombre, pos, uv, tris, c => c.y >= limiteTapa ? Vector3.up : new Vector3(c.x, 0, c.z));
    }

    // =====================================================================
    //  PREFABS
    // =====================================================================

    static GameObject Prim(PrimitiveType tipo, string nombre, Transform padre, Vector3 pos, Vector3 esc, Material mat, Vector3 rot = default)
    {
        var g = GameObject.CreatePrimitive(tipo);
        g.name = nombre;
        g.transform.SetParent(padre, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = rot;
        g.transform.localScale = esc;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static GameObject ConMalla(string nombre, Transform padre, Mesh malla, Material mat, bool colision)
    {
        var g = new GameObject(nombre);
        if (padre != null) g.transform.SetParent(padre, false);
        g.AddComponent<MeshFilter>().sharedMesh = malla;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (colision) g.AddComponent<MeshCollider>().sharedMesh = malla;
        return g;
    }

    static GameObject GuardarPrefab(GameObject g, string nombre)
    {
        var pf = PrefabUtility.SaveAsPrefabAsset(g, $"{Raiz}/Prefabs/{nombre}.prefab");
        Object.DestroyImmediate(g);
        return pf;
    }

    static Prefabs CrearPrefabs(Materiales m)
    {
        var p = new Prefabs();

        // --- Rocas ---
        var mallasRoca = new[] { MallaRoca("Roca_1", 1), MallaRoca("Roca_2", 2), MallaRoca("Roca_3", 3) };
        p.rocas = new GameObject[3];
        for (int i = 0; i < 3; i++)
            p.rocas[i] = GuardarPrefab(ConMalla("Roca_" + (i + 1), null, mallasRoca[i], m.roca, true), "Roca_" + (i + 1));

        // --- Formaciones rocosas ---
        var mMesa = MallaFormacion("Mesa", 32f, 26f, t => (1f - 0.18f * t) * (t > 0.55f && t < 0.7f ? 0.93f : 1f), 21);
        p.mesa = GuardarPrefab(ConMalla("MesaRocosa", null, mMesa, m.estratos, true), "MesaRocosa");
        var mPilar = MallaFormacion("Pilar", 26f, 5f, t => 1f - 0.45f * t + 0.6f * Suave(0.82f, 0.9f, t), 22);
        p.pilar = GuardarPrefab(ConMalla("PilarRocoso", null, mPilar, m.estratos, true), "PilarRocoso");

        // --- Cactus saguaro ---
        {
            var r = new GameObject("CactusSaguaro").transform;
            Prim(PrimitiveType.Cylinder, "Tronco", r, new Vector3(0, 2.5f, 0), new Vector3(0.7f, 2.5f, 0.7f), m.cactus);
            Prim(PrimitiveType.Sphere, "Punta", r, new Vector3(0, 5f, 0), Vector3.one * 0.7f, m.cactus);
            // Brazo derecho
            Prim(PrimitiveType.Cylinder, "BrazoD_H", r, new Vector3(0.45f, 2.2f, 0), new Vector3(0.45f, 0.35f, 0.45f), m.cactus, new Vector3(0, 0, 90));
            Prim(PrimitiveType.Sphere, "CodoD", r, new Vector3(0.8f, 2.2f, 0), Vector3.one * 0.45f, m.cactus);
            Prim(PrimitiveType.Cylinder, "BrazoD_V", r, new Vector3(0.8f, 2.9f, 0), new Vector3(0.45f, 0.7f, 0.45f), m.cactus);
            Prim(PrimitiveType.Sphere, "PuntaD", r, new Vector3(0.8f, 3.6f, 0), Vector3.one * 0.45f, m.cactus);
            // Brazo izquierdo
            Prim(PrimitiveType.Cylinder, "BrazoI_H", r, new Vector3(-0.4f, 3.0f, 0), new Vector3(0.4f, 0.3f, 0.4f), m.cactus, new Vector3(0, 0, 90));
            Prim(PrimitiveType.Sphere, "CodoI", r, new Vector3(-0.7f, 3.0f, 0), Vector3.one * 0.4f, m.cactus);
            Prim(PrimitiveType.Cylinder, "BrazoI_V", r, new Vector3(-0.7f, 3.5f, 0), new Vector3(0.4f, 0.5f, 0.4f), m.cactus);
            Prim(PrimitiveType.Sphere, "PuntaI", r, new Vector3(-0.7f, 4.0f, 0), Vector3.one * 0.4f, m.cactus);
            // Flores
            Prim(PrimitiveType.Sphere, "Flor1", r, new Vector3(0.1f, 5.33f, 0.05f), Vector3.one * 0.14f, m.flor);
            Prim(PrimitiveType.Sphere, "Flor2", r, new Vector3(-0.12f, 5.3f, -0.05f), Vector3.one * 0.12f, m.flor);
            Prim(PrimitiveType.Sphere, "Flor3", r, new Vector3(0.8f, 3.85f, 0), Vector3.one * 0.12f, m.flor);
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 2.6f, 0);
            col.height = 5.3f;
            col.radius = 0.4f;
            p.saguaro = GuardarPrefab(r.gameObject, "CactusSaguaro");
        }

        // --- Cactus barril ---
        {
            var r = new GameObject("CactusBarril").transform;
            Prim(PrimitiveType.Sphere, "Cuerpo", r, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.85f, 0.9f), m.cactus);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                Prim(PrimitiveType.Sphere, "Flor" + i, r, new Vector3(Mathf.Cos(a) * 0.14f, 0.83f, Mathf.Sin(a) * 0.14f), Vector3.one * 0.11f, m.florAmarilla);
            }
            var col = r.gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.4f, 0);
            col.radius = 0.45f;
            p.cactusBarril = GuardarPrefab(r.gameObject, "CactusBarril");
        }

        // --- Nopal ---
        {
            var r = new GameObject("Nopal").transform;
            var hoja = new Vector3(0.7f, 0.85f, 0.18f);
            Prim(PrimitiveType.Sphere, "Penca1", r, new Vector3(0, 0.42f, 0), hoja, m.cactus);
            Prim(PrimitiveType.Sphere, "Penca2", r, new Vector3(0.3f, 0.95f, 0), hoja * 0.9f, m.cactus, new Vector3(0, 0, -25));
            Prim(PrimitiveType.Sphere, "Penca3", r, new Vector3(-0.25f, 0.9f, 0.05f), hoja * 0.85f, m.cactus, new Vector3(0, 20, 30));
            Prim(PrimitiveType.Sphere, "Penca4", r, new Vector3(0.45f, 1.45f, 0), hoja * 0.75f, m.cactus, new Vector3(0, -15, -10));
            Prim(PrimitiveType.Sphere, "Tuna1", r, new Vector3(0.5f, 1.8f, 0), Vector3.one * 0.12f, m.fruto);
            Prim(PrimitiveType.Sphere, "Tuna2", r, new Vector3(0.35f, 1.78f, 0.03f), Vector3.one * 0.11f, m.fruto);
            Prim(PrimitiveType.Sphere, "Tuna3", r, new Vector3(-0.42f, 1.22f, 0.05f), Vector3.one * 0.11f, m.fruto);
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0.1f, 0.9f, 0);
            col.size = new Vector3(1.2f, 1.8f, 0.4f);
            p.nopal = GuardarPrefab(r.gameObject, "Nopal");
        }

        // --- Arbusto seco (centrado en el origen para poder rodar) ---
        {
            var r = new GameObject("ArbustoSeco").transform;
            var local = new Random(5);
            for (int i = 0; i < 26; i++)
            {
                var pos = new Vector3((float)local.NextDouble() - 0.5f, (float)local.NextDouble() - 0.5f, (float)local.NextDouble() - 0.5f) * 0.45f;
                var rot = new Vector3((float)local.NextDouble() * 360, (float)local.NextDouble() * 360, (float)local.NextDouble() * 360);
                Prim(PrimitiveType.Cube, "Rama" + i, r, pos, new Vector3(0.035f, 0.035f, 0.6f + 0.5f * (float)local.NextDouble()), m.arbusto, rot);
            }
            r.gameObject.AddComponent<SphereCollider>().radius = 0.45f;
            p.arbusto = GuardarPrefab(r.gameObject, "ArbustoSeco");
        }

        // --- Cráneo de toro ---
        {
            var r = new GameObject("CraneoToro").transform;
            Prim(PrimitiveType.Sphere, "Craneo", r, new Vector3(0, 0.22f, 0), new Vector3(0.4f, 0.32f, 0.45f), m.hueso);
            Prim(PrimitiveType.Capsule, "Hocico", r, new Vector3(0, 0.15f, 0.3f), new Vector3(0.22f, 0.2f, 0.22f), m.hueso, new Vector3(90, 0, 0));
            foreach (int lado in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Capsule, "Cuerno" + lado, r, new Vector3(0.3f * lado, 0.32f, -0.05f), new Vector3(0.08f, 0.22f, 0.08f), m.hueso, new Vector3(0, 0, -70 * lado));
                Prim(PrimitiveType.Capsule, "Punta" + lado, r, new Vector3(0.5f * lado, 0.45f, -0.05f), new Vector3(0.06f, 0.14f, 0.06f), m.hueso, new Vector3(0, 0, -20 * lado));
                Prim(PrimitiveType.Sphere, "Ojo" + lado, r, new Vector3(0.12f * lado, 0.28f, 0.17f), Vector3.one * 0.1f, m.oscuro);
            }
            Prim(PrimitiveType.Capsule, "Hueso1", r, new Vector3(0.5f, 0.04f, 0.5f), new Vector3(0.07f, 0.3f, 0.07f), m.hueso, new Vector3(90, 30, 0));
            Prim(PrimitiveType.Capsule, "Hueso2", r, new Vector3(-0.4f, 0.04f, 0.6f), new Vector3(0.06f, 0.25f, 0.06f), m.hueso, new Vector3(90, -50, 0));
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.25f, 0.1f);
            col.size = new Vector3(1.1f, 0.5f, 0.8f);
            p.craneo = GuardarPrefab(r.gameObject, "CraneoToro");
        }

        // --- Cartel de madera ---
        {
            var r = new GameObject("CartelMadera").transform;
            Prim(PrimitiveType.Cylinder, "PosteI", r, new Vector3(-0.8f, 1f, 0), new Vector3(0.12f, 1f, 0.12f), m.madera);
            Prim(PrimitiveType.Cylinder, "PosteD", r, new Vector3(0.8f, 1f, 0), new Vector3(0.12f, 1f, 0.12f), m.madera);
            var tabla = Prim(PrimitiveType.Cube, "Tabla", r, new Vector3(0, 1.55f, 0), new Vector3(2f, 0.75f, 0.08f), m.madera, new Vector3(0, 0, 3));
            var texto = new GameObject("Texto");
            texto.transform.SetParent(r, false);
            texto.transform.localPosition = new Vector3(0, 1.55f, -0.05f);
            texto.transform.localEulerAngles = new Vector3(0, 0, 3);
            var tm = texto.AddComponent<TextMesh>();
            var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = fuente;
            tm.text = "BIENVENIDO\nAL DESIERTO";
            tm.fontSize = 64;
            tm.characterSize = 0.04f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.15f, 0.08f, 0.04f);
            texto.GetComponent<MeshRenderer>().sharedMaterial = fuente.material;
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1f, 0);
            col.size = new Vector3(2f, 2f, 0.25f);
            _ = tabla;
            p.cartel = GuardarPrefab(r.gameObject, "CartelMadera");
        }

        // --- Cerca (tramo de 3 m) ---
        {
            var r = new GameObject("Cerca").transform;
            Prim(PrimitiveType.Cylinder, "Poste1", r, new Vector3(0, 0.6f, 0), new Vector3(0.1f, 0.6f, 0.1f), m.madera, new Vector3(0, 0, 2));
            Prim(PrimitiveType.Cylinder, "Poste2", r, new Vector3(3, 0.6f, 0), new Vector3(0.1f, 0.6f, 0.1f), m.madera, new Vector3(3, 0, -2));
            Prim(PrimitiveType.Cube, "Travesano1", r, new Vector3(1.5f, 0.5f, 0), new Vector3(3.1f, 0.1f, 0.05f), m.madera, new Vector3(0, 0, 1));
            Prim(PrimitiveType.Cube, "Travesano2", r, new Vector3(1.5f, 0.95f, 0), new Vector3(3.1f, 0.1f, 0.05f), m.madera, new Vector3(0, 0, -1.5f));
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(1.5f, 0.6f, 0);
            col.size = new Vector3(3.1f, 1.2f, 0.15f);
            p.cerca = GuardarPrefab(r.gameObject, "Cerca");
        }

        // --- Barril ---
        {
            var r = new GameObject("Barril").transform;
            Prim(PrimitiveType.Cylinder, "Cuerpo", r, new Vector3(0, 0.45f, 0), new Vector3(0.6f, 0.45f, 0.6f), m.madera);
            Prim(PrimitiveType.Cylinder, "Aro1", r, new Vector3(0, 0.15f, 0), new Vector3(0.62f, 0.025f, 0.62f), m.metal);
            Prim(PrimitiveType.Cylinder, "Aro2", r, new Vector3(0, 0.75f, 0), new Vector3(0.62f, 0.025f, 0.62f), m.metal);
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.45f, 0);
            col.height = 0.9f;
            col.radius = 0.3f;
            p.barril = GuardarPrefab(r.gameObject, "Barril");
        }

        // --- Tronco para sentarse ---
        {
            var r = new GameObject("TroncoAsiento").transform;
            Prim(PrimitiveType.Cylinder, "Tronco", r, new Vector3(0, 0.2f, 0), new Vector3(0.4f, 0.8f, 0.4f), m.madera, new Vector3(0, 0, 90));
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.2f, 0);
            col.direction = 0;
            col.height = 1.6f;
            col.radius = 0.2f;
            p.tronco = GuardarPrefab(r.gameObject, "TroncoAsiento");
        }

        // --- Fogata (piedras, leña, brasas, fuego, humo, chispas y luz) ---
        {
            var r = new GameObject("Fogata").transform;
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                var piedra = ConMalla("Piedra" + i, r, mallasRoca[i % 3], m.roca, false);
                piedra.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.75f, 0.08f, Mathf.Sin(a) * 0.75f);
                piedra.transform.localScale = Vector3.one * 0.2f;
                piedra.transform.localEulerAngles = new Vector3(0, i * 47f, 0);
            }
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f + 20f;
                var lena = Prim(PrimitiveType.Cylinder, "Lena" + i, r, Vector3.zero, new Vector3(0.12f, 0.5f, 0.12f), m.madera);
                var q = Quaternion.Euler(0, ang, 0);
                lena.transform.localRotation = q * Quaternion.Euler(-30, 0, 0);
                lena.transform.localPosition = q * new Vector3(0, 0.3f, 0.25f);
            }
            Prim(PrimitiveType.Sphere, "Brasas", r, new Vector3(0, 0.04f, 0), new Vector3(0.6f, 0.1f, 0.6f), m.brasa);

            var fuego = Particulas(r, "Fuego", m.fuego, new Vector3(0, 0.1f, 0));
            {
                var mm = fuego.main;
                mm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                mm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                mm.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                mm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.45f, 0.1f));
                mm.maxParticles = 120;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = fuego.emission; em.rateOverTime = 45;
                var sh = fuego.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10; sh.radius = 0.25f;
                var col = fuego.colorOverLifetime; col.enabled = true;
                col.color = Degradado(new[] { new Color(1f, 0.9f, 0.5f), new Color(1f, 0.4f, 0.05f), new Color(0.6f, 0.1f, 0.02f) }, new[] { 0f, 0.8f, 0f });
                var sz = fuego.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            }
            var humo = Particulas(r, "Humo", m.humo, new Vector3(0, 0.9f, 0));
            {
                var mm = humo.main;
                mm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                mm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                mm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                mm.startColor = new Color(0.35f, 0.33f, 0.32f, 0.35f);
                mm.maxParticles = 60;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = humo.emission; em.rateOverTime = 8;
                var sh = humo.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12; sh.radius = 0.2f;
                var col = humo.colorOverLifetime; col.enabled = true;
                col.color = Degradado(new[] { Color.white, Color.white, Color.white }, new[] { 0f, 0.35f, 0f });
                var sz = humo.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.5f));
            }
            var chispas = Particulas(r, "Chispas", m.fuego, new Vector3(0, 0.3f, 0));
            {
                var mm = chispas.main;
                mm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                mm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
                mm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                mm.startColor = new Color(1f, 0.7f, 0.2f);
                mm.maxParticles = 50;
                mm.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = chispas.emission; em.rateOverTime = 10;
                var sh = chispas.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25; sh.radius = 0.2f;
                var ruido = chispas.noise; ruido.enabled = true; ruido.strength = 0.8f; ruido.frequency = 1.5f;
            }

            var luzGo = new GameObject("LuzFuego");
            luzGo.transform.SetParent(r, false);
            luzGo.transform.localPosition = new Vector3(0, 0.8f, 0);
            var luz = luzGo.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.color = new Color(1f, 0.55f, 0.2f);
            luz.range = 10f;
            luz.intensity = 3f;
            luzGo.AddComponent<Parpadeo>();
            p.fogata = GuardarPrefab(r.gameObject, "Fogata");
        }

        // --- Buitre ---
        {
            var r = new GameObject("Buitre").transform;
            Prim(PrimitiveType.Capsule, "Cuerpo", r, Vector3.zero, new Vector3(0.28f, 0.38f, 0.28f), m.plumas, new Vector3(90, 0, 0));
            Prim(PrimitiveType.Cube, "AlaI", r, new Vector3(-0.75f, 0.06f, 0), new Vector3(1.3f, 0.04f, 0.4f), m.plumas, new Vector3(0, 0, -8));
            Prim(PrimitiveType.Cube, "AlaD", r, new Vector3(0.75f, 0.06f, 0), new Vector3(1.3f, 0.04f, 0.4f), m.plumas, new Vector3(0, 0, 8));
            Prim(PrimitiveType.Cube, "Cola", r, new Vector3(0, 0, -0.45f), new Vector3(0.3f, 0.03f, 0.3f), m.plumas);
            Prim(PrimitiveType.Sphere, "Cabeza", r, new Vector3(0, 0.05f, 0.45f), Vector3.one * 0.15f, m.cabeza);
            Prim(PrimitiveType.Sphere, "Pico", r, new Vector3(0, 0.03f, 0.55f), new Vector3(0.05f, 0.05f, 0.1f), m.hueso);
            r.gameObject.AddComponent<VueloCircular>();
            p.buitre = GuardarPrefab(r.gameObject, "Buitre");
        }

        return p;
    }

    static ParticleSystem Particulas(Transform padre, string nombre, Material mat, Vector3 pos)
    {
        var g = new GameObject(nombre);
        g.transform.SetParent(padre, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = new Vector3(-90, 0, 0); // emitir hacia arriba
        var ps = g.AddComponent<ParticleSystem>();
        g.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }

    static ParticleSystem.MinMaxGradient Degradado(Color[] colores, float[] alfas)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(colores[0], 0f), new GradientColorKey(colores[1], 0.5f), new GradientColorKey(colores[2], 1f) },
            new[] { new GradientAlphaKey(alfas[0], 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(alfas[1], 0.6f), new GradientAlphaKey(alfas[2], 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    // =====================================================================
    //  TERRENO
    // =====================================================================

    /// <summary>Cauce seco de un río: 1 en el centro del cauce, 0 fuera.</summary>
    static float Cauce(float nx, float nz, float ancho = 0.025f)
    {
        float cz = 0.42f + 0.08f * Mathf.Sin(nx * Mathf.PI * 2f * 1.3f) + 0.03f * Mathf.Sin(nx * Mathf.PI * 2f * 3.1f + 1f);
        return 1f - Suave(0f, ancho, Mathf.Abs(nz - cz));
    }

    static float DistCentro(float nx, float nz) => Vector2.Distance(new Vector2(nx, nz), new Vector2(0.5f, 0.5f)) * 2f;

    static float Altura(float nx, float nz)
    {
        float d = DistCentro(nx, nz);
        float dunas = 0.02f + 0.035f * Mathf.PerlinNoise(nx * 4f + 10f, nz * 4f + 10f)
                      + 0.018f * Mathf.Pow(Mathf.Abs(Mathf.Sin((nx * 9f + nz * 3f + Mathf.PerlinNoise(nx * 3f, nz * 3f) * 1.5f) * Mathf.PI)), 2f)
                               * Mathf.PerlinNoise(nx * 2f + 40f, nz * 2f + 40f);
        float ruidoMonte = 0.55f * Mathf.PerlinNoise(nx * 5f + 90f, nz * 5f + 90f) + 0.3f * Mathf.PerlinNoise(nx * 11f, nz * 11f) + 0.15f * Mathf.PerlinNoise(nx * 23f, nz * 23f);
        float montanas = Suave(0.68f, 1.05f, d) * (0.3f + 0.7f * ruidoMonte) * 0.85f;
        float rio = Cauce(nx, nz) * 0.014f * (1f - Suave(0.6f, 0.75f, d));
        return Mathf.Max(0.002f, dunas + montanas - rio);
    }

    static TerrainLayer Capa(string nombre, Texture2D tex, Texture2D normal, float tam)
    {
        var l = new TerrainLayer
        {
            diffuseTexture = tex,
            normalMapTexture = normal,
            tileSize = new Vector2(tam, tam),
            normalScale = 1f,
            smoothness = 0.05f,
        };
        AssetDatabase.CreateAsset(l, $"{Raiz}/Terreno/{nombre}.terrainlayer");
        return l;
    }

    static Terrain CrearTerreno(Texturas t, Materiales m)
    {
        var td = new TerrainData { heightmapResolution = ResAlt };
        td.size = new Vector3(Tam, Alto, Tam);

        var h = new float[ResAlt, ResAlt];
        for (int z = 0; z < ResAlt; z++)
            for (int x = 0; x < ResAlt; x++)
                h[z, x] = Altura(x / (float)(ResAlt - 1), z / (float)(ResAlt - 1));
        td.SetHeights(0, 0, h);

        // --- Texturas del suelo (0 arena, 1 tierra agrietada, 2 roca, 3 pedregoso) ---
        td.terrainLayers = new[]
        {
            Capa("Arena", t.arena, t.arenaN, 10f),
            Capa("TierraAgrietada", t.grieta, t.grietaN, 9f),
            Capa("Roca", t.roca, t.rocaN, 14f),
            Capa("Pedregoso", t.piedras, t.piedrasN, 5f),
        };
        td.alphamapResolution = ResMapa;
        var a = new float[ResMapa, ResMapa, 4];
        for (int z = 0; z < ResMapa; z++)
            for (int x = 0; x < ResMapa; x++)
            {
                float nx = (x + 0.5f) / ResMapa, nz = (z + 0.5f) / ResMapa;
                float pendiente = td.GetSteepness(nx, nz);
                float alt = td.GetInterpolatedHeight(nx, nz) / Alto;
                float roca = Mathf.Max(Suave(22f, 36f, pendiente), Suave(0.16f, 0.3f, alt));
                float grieta = Mathf.Max(Cauce(nx, nz, 0.05f), Suave(0.62f, 0.75f, Mathf.PerlinNoise(nx * 8f + 30f, nz * 8f + 30f)));
                float piedras = Suave(0.6f, 0.75f, Mathf.PerlinNoise(nx * 12f + 70f, nz * 12f + 70f)) * 0.85f;
                float resto = 1f - roca;
                float wGrieta = resto * grieta;
                float resto2 = resto - wGrieta;
                float wPiedras = resto2 * piedras;
                a[z, x, 0] = resto2 - wPiedras;
                a[z, x, 1] = wGrieta;
                a[z, x, 2] = roca;
                a[z, x, 3] = wPiedras;
            }
        td.SetAlphamaps(0, 0, a);

        // --- Hierba seca (detalles) ---
        td.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = t.hierba,
                renderMode = DetailRenderMode.Grass,
                usePrototypeMesh = false,
                useInstancing = false,
                minWidth = 0.6f, maxWidth = 1.2f,
                minHeight = 0.35f, maxHeight = 0.8f,
                noiseSpread = 0.3f,
                healthyColor = new Color(0.85f, 0.72f, 0.48f),
                dryColor = new Color(0.68f, 0.52f, 0.32f),
            }
        };
        td.SetDetailResolution(ResDet, 32);
        td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        var det = new int[ResDet, ResDet];
        var local = new Random(99);
        for (int z = 0; z < ResDet; z++)
            for (int x = 0; x < ResDet; x++)
            {
                float nx = (x + 0.5f) / ResDet, nz = (z + 0.5f) / ResDet;
                if (td.GetSteepness(nx, nz) > 20f || Cauce(nx, nz, 0.04f) > 0.3f || DistCentro(nx, nz) > 0.8f) continue;
                if (Vector2.Distance(new Vector2(nx * Tam, nz * Tam), new Vector2(Campamento.x, Campamento.z)) < 7f) continue;
                float mata = Mathf.PerlinNoise(nx * 25f + 5f, nz * 25f + 5f);
                if (mata > 0.66f) det[z, x] = 1 + local.Next(3);
                else if (local.NextDouble() < 0.015) det[z, x] = 1;
            }
        td.SetDetailLayer(0, 0, 0, det);
        td.wavingGrassTint = new Color(0.9f, 0.8f, 0.6f);
        td.wavingGrassStrength = 0.4f;
        td.wavingGrassAmount = 0.4f;
        td.wavingGrassSpeed = 0.5f;

        AssetDatabase.CreateAsset(td, Raiz + "/Terreno/TerrenoDesierto.asset");

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terreno";
        var ter = go.GetComponent<Terrain>();
        ter.materialTemplate = m.terreno;
        ter.drawInstanced = true;
        ter.heightmapPixelError = 4f;
        ter.basemapDistance = 1000f;
        ter.detailObjectDistance = 120f;
        ter.detailObjectDensity = 1f;
        return ter;
    }

    // =====================================================================
    //  AMBIENTE
    // =====================================================================

    static void CrearAmbiente(Materiales m)
    {
        var amb = new GameObject("Ambiente").transform;

        // Sol bajo y cálido (como la ilustración)
        var solGo = new GameObject("Sol");
        solGo.transform.SetParent(amb, false);
        solGo.transform.rotation = Quaternion.Euler(16f, -30f, 0f);
        var sol = solGo.AddComponent<Light>();
        sol.type = LightType.Directional;
        sol.color = new Color(1f, 0.83f, 0.62f);
        sol.intensity = 1.7f;
        sol.shadows = LightShadows.Soft;
        sol.shadowStrength = 0.85f;

        RenderSettings.sun = sol;
        RenderSettings.skybox = m.cielo;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.85f, 0.70f, 0.55f);
        RenderSettings.ambientEquatorColor = new Color(0.75f, 0.55f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.45f, 0.30f, 0.20f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.93f, 0.74f, 0.52f);
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 560f;

        // Viento
        var viento = new GameObject("Viento");
        viento.transform.SetParent(amb, false);
        viento.transform.rotation = Quaternion.LookRotation(new Vector3(1f, 0f, 0.3f));
        var wz = viento.AddComponent<WindZone>();
        wz.mode = WindZoneMode.Directional;
        wz.windMain = 0.6f;
        wz.windTurbulence = 0.4f;

        // Post-procesado
        var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(perfil, Raiz + "/Ambiente/PostProcesado.asset");
        var bloom = perfil.Add<Bloom>(true);
        bloom.intensity.Override(0.6f);
        bloom.threshold.Override(1f);
        var ca = perfil.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.3f);
        ca.contrast.Override(12f);
        ca.saturation.Override(15f);
        ca.colorFilter.Override(new Color(1f, 0.95f, 0.88f));
        var vi = perfil.Add<Vignette>(true);
        vi.intensity.Override(0.28f);
        var tm = perfil.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);
        foreach (var comp in perfil.components)
        {
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, perfil);
        }
        EditorUtility.SetDirty(perfil);

        var vol = new GameObject("PostProcesado");
        vol.transform.SetParent(amb, false);
        var v = vol.AddComponent<Volume>();
        v.isGlobal = true;
        v.sharedProfile = perfil;
    }

    // =====================================================================
    //  DECORACIÓN
    // =====================================================================

    static readonly List<Vector3> ocupados = new List<Vector3>();

    static float Suelo(Terrain ter, float x, float z) => ter.SampleHeight(new Vector3(x, 0, z)) + ter.transform.position.y;

    static GameObject Colocar(GameObject prefab, Transform padre, Terrain ter, float x, float z, float escala, float yaw, float hundir = 0f, bool alinear = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        var rot = Quaternion.Euler(0, yaw, 0);
        if (alinear)
        {
            var n = ter.terrainData.GetInterpolatedNormal(x / Tam, z / Tam);
            rot = Quaternion.FromToRotation(Vector3.up, n) * rot;
        }
        go.transform.SetPositionAndRotation(new Vector3(x, Suelo(ter, x, z) - hundir, z), rot);
        go.transform.localScale = Vector3.one * escala;
        return go;
    }

    static bool Libre(Terrain ter, float x, float z, float sep, float maxPendiente)
    {
        float nx = x / Tam, nz = z / Tam;
        if (ter.terrainData.GetSteepness(nx, nz) > maxPendiente) return false;
        if (Cauce(nx, nz) > 0.2f) return false;
        if (Vector2.Distance(new Vector2(x, z), new Vector2(Campamento.x, Campamento.z)) < 14f) return false;
        foreach (var o in ocupados)
            if ((o.x - x) * (o.x - x) + (o.z - z) * (o.z - z) < sep * sep) return false;
        return true;
    }

    static void Esparcir(GameObject[] prefabs, Transform padre, Terrain ter, int cantidad, float rMin, float rMax,
                         float eMin, float eMax, float sep, float maxPendiente, bool alinear, float hundir)
    {
        int puestos = 0;
        for (int intento = 0; intento < cantidad * 30 && puestos < cantidad; intento++)
        {
            float ang = R(0, Mathf.PI * 2f);
            float rad = Mathf.Sqrt(R(rMin * rMin, rMax * rMax));
            float x = Centro.x + Mathf.Cos(ang) * rad, z = Centro.z + Mathf.Sin(ang) * rad;
            if (!Libre(ter, x, z, sep, maxPendiente)) continue;
            float e = R(eMin, eMax);
            Colocar(prefabs[rnd.Next(prefabs.Length)], padre, ter, x, z, e, R(0, 360), hundir * e, alinear);
            ocupados.Add(new Vector3(x, 0, z));
            puestos++;
        }
    }

    static Transform Grupo(string nombre) => new GameObject(nombre).transform;

    static void Decorar(Terrain ter, Prefabs p)
    {
        ocupados.Clear();

        // Formaciones rocosas en el horizonte medio
        var gForm = Grupo("Formaciones Rocosas");
        for (int i = 0; i < 7; i++)
        {
            float ang = i / 7f * Mathf.PI * 2f + R(-0.3f, 0.3f);
            float dist = R(115f, 165f);
            float x = Centro.x + Mathf.Cos(ang) * dist, z = Centro.z + Mathf.Sin(ang) * dist;
            if (Cauce(x / Tam, z / Tam, 0.08f) > 0.1f) { x += 25f; z += 25f; }
            var pf = i % 3 == 2 ? p.pilar : p.mesa;
            Colocar(pf, gForm, ter, x, z, R(0.7f, 1.2f), R(0, 360), 2f);
            ocupados.Add(new Vector3(x, 0, z));
        }
        for (int i = 0; i < 8; i++)
        {
            float ang = R(0, Mathf.PI * 2f), dist = R(55f, 150f);
            float x = Centro.x + Mathf.Cos(ang) * dist, z = Centro.z + Mathf.Sin(ang) * dist;
            if (!Libre(ter, x, z, 30f, 30f)) continue;
            Colocar(p.pilar, gForm, ter, x, z, R(0.5f, 1.1f), R(0, 360), 1f);
            ocupados.Add(new Vector3(x, 0, z));
        }

        var gRocas = Grupo("Rocas");
        Esparcir(p.rocas, gRocas, ter, 160, 5f, 195f, 0.4f, 2.8f, 3f, 45f, true, 0.15f);

        var gCactus = Grupo("Cactus");
        Esparcir(new[] { p.saguaro }, gCactus, ter, 70, 10f, 180f, 0.8f, 1.5f, 6f, 25f, false, 0.1f);
        Esparcir(new[] { p.cactusBarril }, gCactus, ter, 45, 8f, 180f, 0.8f, 1.4f, 2.5f, 25f, false, 0.05f);
        Esparcir(new[] { p.nopal }, gCactus, ter, 45, 8f, 180f, 0.8f, 1.5f, 3f, 25f, false, 0.05f);

        var gSeco = Grupo("Vegetacion Seca");
        Esparcir(new[] { p.arbusto }, gSeco, ter, 60, 5f, 180f, 0.7f, 1.3f, 2f, 25f, false, -0.4f);
        Esparcir(new[] { p.craneo }, gSeco, ter, 5, 20f, 150f, 0.9f, 1.2f, 5f, 15f, true, 0f);

        // Arbustos que ruedan con el viento
        var gRodantes = Grupo("Arbustos Rodantes");
        for (int i = 0; i < 6; i++)
        {
            float x = Campamento.x + R(-60f, 20f), z = Campamento.z + R(-40f, 40f);
            var a = Colocar(p.arbusto, gRodantes, ter, x, z, R(0.8f, 1.2f), R(0, 360), -0.45f);
            var rod = a.AddComponent<RodarArbusto>();
            rod.velocidad = R(2f, 4.5f);
        }

        // Campamento abandonado
        var gCamp = Grupo("Campamento");
        var c = Campamento;
        Colocar(p.fogata, gCamp, ter, c.x, c.z, 1f, 0f, 0.05f);
        Colocar(p.tronco, gCamp, ter, c.x + 1.8f, c.z + 0.6f, 1f, 70f, 0.05f);
        Colocar(p.tronco, gCamp, ter, c.x - 1.6f, c.z + 1.1f, 1f, 120f, 0.05f);
        Colocar(p.barril, gCamp, ter, c.x + 3.5f, c.z - 2f, 1f, 10f);
        Colocar(p.barril, gCamp, ter, c.x + 4.3f, c.z - 1.3f, 1f, 50f);
        var tumbado = Colocar(p.barril, gCamp, ter, c.x + 4.6f, c.z - 3f, 1f, 0f, -0.3f);
        tumbado.transform.rotation = Quaternion.Euler(90f, 35f, 0f);
        Colocar(p.cartel, gCamp, ter, c.x - 4f, c.z - 5f, 1f, 160f, 0.3f);
        Colocar(p.craneo, gCamp, ter, c.x + 2.2f, c.z - 3.5f, 1f, 200f, 0f, true);
        for (int i = 0; i < 6; i++)
        {
            if (i == 3)
            {
                var caida = Colocar(p.cerca, gCamp, ter, c.x - 12f + i * 3.1f, c.z + 9f, 1f, R(-4, 4), -0.05f);
                caida.transform.rotation = Quaternion.Euler(80f, 5f, 0f);
                continue;
            }
            Colocar(p.cerca, gCamp, ter, c.x - 12f + i * 3.1f, c.z + 9f, 1f, R(-4, 4), 0.1f);
        }

        // Buitres volando en círculos
        var gFauna = Grupo("Fauna");
        for (int i = 0; i < 3; i++)
        {
            var b = (GameObject)PrefabUtility.InstantiatePrefab(p.buitre, gFauna);
            b.transform.localScale = Vector3.one * 1.5f;
            var vuelo = b.GetComponent<VueloCircular>();
            vuelo.centro = new Vector3(c.x - 20f, Suelo(ter, c.x, c.z) + 40f + i * 6f, c.z - 30f);
            vuelo.radio = 18f + i * 8f;
            vuelo.velocidad = 0.22f + i * 0.05f;
            vuelo.fase = i * 2.1f;
            vuelo.Colocar(0f);
        }
    }

    // =====================================================================
    //  JUGADOR Y CÁMARA
    // =====================================================================

    static Camera CrearJugador(Terrain ter, Materiales m)
    {
        float x = Campamento.x - 3f, z = Campamento.z + 7f;
        var j = new GameObject("Jugador");
        j.transform.SetPositionAndRotation(new Vector3(x, Suelo(ter, x, z) + 0.1f, z), Quaternion.Euler(0, 160f, 0));
        var cc = j.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0, 0.9f, 0);

        var camGo = new GameObject("Camara");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(j.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1500f;
        camGo.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var pp = j.AddComponent<PrimeraPersona>();
        pp.camara = camGo.transform;

        // Polvo en suspensión que acompaña al jugador
        var polvoGo = new GameObject("PolvoEnElViento");
        polvoGo.transform.SetParent(j.transform, false);
        polvoGo.transform.localPosition = new Vector3(0, 4f, 0);
        var ps = polvoGo.AddComponent<ParticleSystem>();
        polvoGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = m.polvo;
        var mm = ps.main;
        mm.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
        mm.startSpeed = 0f;
        mm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        mm.startColor = new Color(0.92f, 0.76f, 0.55f, 0.12f);
        mm.maxParticles = 500;
        mm.simulationSpace = ParticleSystemSimulationSpace.World;
        mm.prewarm = true;
        var em = ps.emission; em.rateOverTime = 45;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(120f, 8f, 120f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(3f, 7f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        vel.z = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = Degradado(new[] { Color.white, Color.white, Color.white }, new[] { 0f, 1f, 0f });

        return cam;
    }
}
