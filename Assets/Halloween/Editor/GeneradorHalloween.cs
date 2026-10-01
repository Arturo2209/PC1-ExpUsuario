using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Genera el escenario de Halloween desde el menú  Halloween > Generar escenario.
/// Crea las texturas, materiales, prefabs, el terreno y coloca la decoración.
/// </summary>
public static class GeneradorHalloween
{
    const string Raiz = "Assets/Halloween";
    const float Tam = 200f, Alto = 30f;
    const int ResAlt = 257, ResMapa = 256;

    static System.Random rnd;
    static float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    // Materiales
    static Material mCorteza, mCalabaza, mLuzCalabaza, mTallo, mPiedra, mMetal, mLuzFarol, mMadera,
        mLuna, mNegro, mFantasma, mPocion, mBrillo, mNiebla, mCielo, mHojas, mTunica, mPiel, mPaja;

    // Prefabs
    static GameObject pCalabaza, pLapida, pCruz, pFarol, pCerca, pMurcielago, pFantasma, pCaldero, pBruja, pBrujaVolando;
    static GameObject[] pArboles;

    [MenuItem("Halloween/Generar escenario")]
    public static void Generar()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        rnd = new System.Random(31);
        try
        {
            EditorUtility.DisplayProgressBar("Halloween", "Creando texturas y materiales...", 0.1f);
            CrearCarpetas();
            var pasto = CrearTextura("Pasto", new Color(0.07f, 0.20f, 0.16f), new Color(0.14f, 0.34f, 0.24f), 6f);
            var tierra = CrearTextura("Tierra", new Color(0.20f, 0.14f, 0.11f), new Color(0.36f, 0.27f, 0.20f), 10f);
            var corteza = CrearTextura("Corteza", new Color(0.10f, 0.07f, 0.06f), new Color(0.26f, 0.19f, 0.15f), 14f);
            var suave = CrearTexturaSuave();
            CrearMateriales(corteza, suave);

            var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EditorUtility.DisplayProgressBar("Halloween", "Creando prefabs...", 0.4f);
            CrearPrefabs();
            EditorUtility.DisplayProgressBar("Halloween", "Creando terreno...", 0.6f);
            var terreno = CrearTerreno(pasto, tierra);
            EditorUtility.DisplayProgressBar("Halloween", "Decorando...", 0.8f);
            CrearAmbiente();
            Decorar(terreno);
            var cam = CrearJugador(terreno);

            string ruta = Raiz + "/Escenas/Halloween.unity";
            EditorSceneManager.SaveScene(escena, ruta);
            AssetDatabase.SaveAssets();
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.AlignViewToObject(cam.transform);
            Debug.Log("Escenario de Halloween generado en " + ruta);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void CrearCarpetas()
    {
        foreach (var sub in new[] { "Texturas", "Materiales", "Prefabs", "Terreno", "Escenas" })
            if (!AssetDatabase.IsValidFolder(Raiz + "/" + sub)) AssetDatabase.CreateFolder(Raiz, sub);
    }

    // ------------------------------------------------------------------
    //  TEXTURAS
    // ------------------------------------------------------------------

    /// <summary>Ruido Perlin que se repite sin costuras (u,v entre 0 y 1).</summary>
    static float RuidoRepetible(float u, float v, float f)
    {
        float a = Mathf.PerlinNoise(100 + u * f, 100 + v * f);
        float b = Mathf.PerlinNoise(100 + (u - 1) * f, 100 + v * f);
        float c = Mathf.PerlinNoise(100 + u * f, 100 + (v - 1) * f);
        float d = Mathf.PerlinNoise(100 + (u - 1) * f, 100 + (v - 1) * f);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    static Texture2D CrearTextura(string nombre, Color oscuro, Color claro, float frecuencia)
    {
        const int s = 256;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                float n = 0.6f * RuidoRepetible(u, v, frecuencia) + 0.4f * RuidoRepetible(u, v, frecuencia * 4f);
                px[y * s + x] = Color.Lerp(oscuro, claro, n);
            }
        return GuardarTextura(nombre, px, s, false);
    }

    static Texture2D CrearTexturaSuave()
    {
        const int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                float a = Mathf.Clamp01(1f - d);
                px[y * s + x] = new Color(1, 1, 1, a * a);
            }
        return GuardarTextura("ParticulaSuave", px, s, true);
    }

    static Texture2D GuardarTextura(string nombre, Color[] px, int s, bool transparente)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        string ruta = $"{Raiz}/Texturas/{nombre}.png";
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
        imp.alphaIsTransparency = transparente;
        imp.wrapMode = transparente ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }

    // ------------------------------------------------------------------
    //  MATERIALES
    // ------------------------------------------------------------------

    static Material Mat(string nombre, Color color, Texture2D tex = null, Color? emision = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.15f);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        if (emision.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emision.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
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
        m.SetFloat("_Blend", aditivo ? 2 : 0);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(aditivo ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0);
        m.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, $"{Raiz}/Materiales/{nombre}.mat");
        return m;
    }

    static void CrearMateriales(Texture2D corteza, Texture2D suave)
    {
        mCorteza = Mat("Corteza", Color.white, corteza);
        mCalabaza = Mat("Calabaza", new Color(0.95f, 0.45f, 0.08f));
        mLuzCalabaza = Mat("LuzCalabaza", new Color(1f, 0.8f, 0.2f), null, new Color(1f, 0.65f, 0.1f) * 4f);
        mTallo = Mat("Tallo", new Color(0.25f, 0.35f, 0.12f));
        mPiedra = Mat("Piedra", new Color(0.42f, 0.45f, 0.47f));
        mMetal = Mat("MetalNegro", new Color(0.06f, 0.06f, 0.07f));
        mLuzFarol = Mat("LuzFarol", new Color(1f, 0.9f, 0.6f), null, new Color(1f, 0.8f, 0.45f) * 4f);
        mMadera = Mat("MaderaVieja", new Color(0.20f, 0.15f, 0.12f), corteza);
        mLuna = Mat("Luna", new Color(0.9f, 0.95f, 1f), null, new Color(0.8f, 0.9f, 1f) * 12f); // muy brillante para que se vea a través de la niebla
        mNegro = Mat("Negro", new Color(0.03f, 0.03f, 0.04f));
        mFantasma = Mat("Fantasma", new Color(0.9f, 0.95f, 1f), null, new Color(0.35f, 0.45f, 0.5f));
        mPocion = Mat("Pocion", new Color(0.3f, 1f, 0.3f), null, new Color(0.2f, 1f, 0.25f) * 3f);
        mHojas = Mat("HojasOscuras", new Color(0.05f, 0.17f, 0.15f));
        mTunica = Mat("Tunica", new Color(0.24f, 0.10f, 0.34f));
        mPiel = Mat("PielBruja", new Color(0.45f, 0.70f, 0.35f));
        mPaja = Mat("Paja", new Color(0.75f, 0.60f, 0.25f));
        mBrillo = MatParticula("ParticulaBrillo", suave, true);
        mNiebla = MatParticula("ParticulaNiebla", suave, false);

        // Cielo nocturno azul verdoso
        mCielo = new Material(Shader.Find("Skybox/Procedural"));
        mCielo.SetFloat("_SunDisk", 0);
        mCielo.SetFloat("_AtmosphereThickness", 0.7f);
        mCielo.SetColor("_SkyTint", new Color(0.2f, 0.55f, 0.65f));
        mCielo.SetColor("_GroundColor", new Color(0.03f, 0.08f, 0.09f));
        mCielo.SetFloat("_Exposure", 0.35f);
        AssetDatabase.CreateAsset(mCielo, Raiz + "/Materiales/CieloNoche.mat");
    }

    // ------------------------------------------------------------------
    //  PREFABS (hechos con figuras básicas)
    // ------------------------------------------------------------------

    static GameObject Pieza(PrimitiveType tipo, string nombre, Transform padre, Vector3 pos, Vector3 escala, Material mat, Vector3 rot = default)
    {
        var g = GameObject.CreatePrimitive(tipo);
        g.name = nombre;
        g.transform.SetParent(padre, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = rot;
        g.transform.localScale = escala;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    /// <summary>Cilindro que va desde "inicio" en la dirección "dir" (para troncos y ramas).</summary>
    static Vector3 Rama(Transform padre, Vector3 inicio, Vector3 dir, float largo, float grosor)
    {
        dir.Normalize();
        var g = Pieza(PrimitiveType.Cylinder, "Rama", padre, inicio + dir * largo * 0.5f, new Vector3(grosor, largo * 0.5f, grosor), mCorteza);
        g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
        return inicio + dir * largo;
    }

    static Light LuzPunto(Transform padre, Vector3 pos, Color color, float rango, float intensidad, bool parpadea)
    {
        var g = new GameObject("Luz");
        g.transform.SetParent(padre, false);
        g.transform.localPosition = pos;
        var l = g.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = rango;
        l.intensity = intensidad;
        if (parpadea)
        {
            var p = g.AddComponent<Parpadeo>();
            p.intensidadBase = intensidad;
            p.variacion = intensidad * 0.3f;
        }
        return l;
    }

    static GameObject CrearBruja(bool volando)
    {
        var r = new GameObject(volando ? "BrujaVolando" : "Bruja").transform;
        var c = new GameObject("Cuerpo").transform;
        c.SetParent(r, false);

        Pieza(PrimitiveType.Cylinder, "Falda", c, new Vector3(0, 0.55f, 0), new Vector3(0.75f, 0.55f, 0.75f), mTunica);
        Pieza(PrimitiveType.Capsule, "Torso", c, new Vector3(0, 1.15f, 0), new Vector3(0.5f, 0.42f, 0.5f), mTunica);
        Pieza(PrimitiveType.Capsule, "BrazoIzq", c, new Vector3(-0.3f, 1.2f, 0.15f), new Vector3(0.13f, 0.3f, 0.13f), mTunica, new Vector3(60, 0, 15));
        Pieza(PrimitiveType.Capsule, "BrazoDer", c, new Vector3(0.3f, 1.2f, 0.15f), new Vector3(0.13f, 0.3f, 0.13f), mTunica, new Vector3(60, 0, -15));
        Pieza(PrimitiveType.Sphere, "Cabeza", c, new Vector3(0, 1.68f, 0), Vector3.one * 0.42f, mPiel);
        Pieza(PrimitiveType.Capsule, "Nariz", c, new Vector3(0, 1.66f, 0.24f), new Vector3(0.07f, 0.1f, 0.07f), mPiel, new Vector3(90, 0, 0));
        Pieza(PrimitiveType.Sphere, "OjoIzq", c, new Vector3(-0.08f, 1.74f, 0.18f), Vector3.one * 0.06f, mLuzCalabaza);
        Pieza(PrimitiveType.Sphere, "OjoDer", c, new Vector3(0.08f, 1.74f, 0.18f), Vector3.one * 0.06f, mLuzCalabaza);
        // Sombrero puntiagudo: ala + tres cilindros cada vez más finos
        Pieza(PrimitiveType.Cylinder, "Ala", c, new Vector3(0, 1.87f, 0), new Vector3(0.8f, 0.02f, 0.8f), mNegro);
        Pieza(PrimitiveType.Cylinder, "Sombrero1", c, new Vector3(0, 2.0f, 0), new Vector3(0.4f, 0.12f, 0.4f), mNegro);
        Pieza(PrimitiveType.Cylinder, "Sombrero2", c, new Vector3(0, 2.22f, -0.02f), new Vector3(0.27f, 0.12f, 0.27f), mNegro, new Vector3(-8, 0, 0));
        Pieza(PrimitiveType.Cylinder, "Sombrero3", c, new Vector3(0, 2.43f, -0.07f), new Vector3(0.14f, 0.12f, 0.14f), mNegro, new Vector3(-18, 0, 0));

        if (volando)
        {
            // Sentada sobre la escoba, que apunta hacia adelante
            c.localPosition = new Vector3(0, -0.7f, 0);
            c.localEulerAngles = new Vector3(12, 0, 0);
            Pieza(PrimitiveType.Cylinder, "Palo", r, Vector3.zero, new Vector3(0.06f, 1.2f, 0.06f), mMadera, new Vector3(90, 0, 0));
            Pieza(PrimitiveType.Cylinder, "Cerdas", r, new Vector3(0, 0, -1.3f), new Vector3(0.28f, 0.3f, 0.28f), mPaja, new Vector3(90, 0, 0));
            r.gameObject.AddComponent<VueloCircular>().inclinacion = 15f;
        }
        else
        {
            // De pie, con la escoba en la mano
            Pieza(PrimitiveType.Cylinder, "Palo", r, new Vector3(0.42f, 1.0f, 0.3f), new Vector3(0.05f, 1.0f, 0.05f), mMadera);
            Pieza(PrimitiveType.Cylinder, "Cerdas", r, new Vector3(0.42f, 0.22f, 0.3f), new Vector3(0.24f, 0.22f, 0.24f), mPaja);
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 1.1f, 0);
            col.height = 2.2f;
            col.radius = 0.4f;
        }
        return Guardar(r, r.name);
    }

    static GameObject Guardar(Transform raiz, string nombre)
    {
        var pf = PrefabUtility.SaveAsPrefabAsset(raiz.gameObject, $"{Raiz}/Prefabs/{nombre}.prefab");
        Object.DestroyImmediate(raiz.gameObject);
        return pf;
    }

    static void CrearPrefabs()
    {
        // --- Calabaza con cara iluminada ---
        {
            var r = new GameObject("Calabaza").transform;
            Pieza(PrimitiveType.Sphere, "Cuerpo", r, new Vector3(0, 0.4f, 0), new Vector3(1f, 0.8f, 1f), mCalabaza);
            Pieza(PrimitiveType.Cylinder, "Tallo", r, new Vector3(0, 0.85f, 0), new Vector3(0.1f, 0.1f, 0.1f), mTallo, new Vector3(0, 0, 12));
            Pieza(PrimitiveType.Cube, "OjoIzq", r, new Vector3(-0.17f, 0.52f, 0.42f), new Vector3(0.14f, 0.14f, 0.08f), mLuzCalabaza, new Vector3(0, 0, 45));
            Pieza(PrimitiveType.Cube, "OjoDer", r, new Vector3(0.17f, 0.52f, 0.42f), new Vector3(0.14f, 0.14f, 0.08f), mLuzCalabaza, new Vector3(0, 0, 45));
            Pieza(PrimitiveType.Cube, "Nariz", r, new Vector3(0, 0.4f, 0.47f), new Vector3(0.07f, 0.07f, 0.08f), mLuzCalabaza, new Vector3(0, 0, 45));
            Pieza(PrimitiveType.Cube, "Boca", r, new Vector3(0, 0.25f, 0.43f), new Vector3(0.42f, 0.08f, 0.08f), mLuzCalabaza);
            LuzPunto(r, new Vector3(0, 0.45f, 0.6f), new Color(1f, 0.55f, 0.15f), 5f, 2.5f, true);
            var col = r.gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.4f, 0);
            col.radius = 0.45f;
            pCalabaza = Guardar(r, "Calabaza");
        }

        // --- Árboles secos (2 variantes) ---
        pArboles = new GameObject[2];
        for (int v = 0; v < 2; v++)
        {
            var azar = new System.Random(10 + v);
            float A(float a, float b) => a + (float)azar.NextDouble() * (b - a);
            var r = new GameObject("ArbolSeco_" + (v + 1)).transform;
            // Tronco grueso en dos tramos (más ancho abajo)
            Vector3 medio = Rama(r, new Vector3(0, -0.3f, 0), new Vector3(A(-0.08f, 0.08f), 1, A(-0.08f, 0.08f)), 2.6f, 1.0f);
            Vector3 copa = Rama(r, medio - Vector3.up * 0.3f, new Vector3(A(-0.15f, 0.15f), 1, A(-0.15f, 0.15f)), 2.4f, 0.7f);
            // Raíces
            for (int i = 0; i < 5; i++)
            {
                Vector3 lado = Quaternion.Euler(0, i * 72f + A(-15, 15), 0) * Vector3.forward;
                Rama(r, new Vector3(0, 0.5f, 0), lado - Vector3.up * 0.45f, 1.5f, 0.35f);
            }
            // Ramas gruesas, cada una con dos ramitas (y hojas en la variante 2)
            int ramas = 6 + v;
            for (int i = 0; i < ramas; i++)
            {
                float ang = i * (360f / ramas) + A(-20, 20);
                Vector3 lado = Quaternion.Euler(0, ang, 0) * Vector3.forward;
                Vector3 desde = new Vector3(0, A(2.3f, 4.2f), 0);
                Vector3 punta = Rama(r, desde, lado + Vector3.up * A(0.5f, 1.2f), A(2.2f, 3.2f), 0.36f);
                Vector3 cruz = Quaternion.Euler(0, 90, 0) * lado;
                Vector3 p1 = Rama(r, punta, lado * A(0.3f, 1f) + Vector3.up + cruz * A(0.2f, 0.7f), A(1f, 1.6f), 0.18f);
                Rama(r, punta, lado * A(0.3f, 1f) + Vector3.up * 0.6f - cruz * A(0.2f, 0.7f), A(1f, 1.6f), 0.18f);
                if (v == 1)
                    Pieza(PrimitiveType.Sphere, "Hojas", r, p1, new Vector3(A(2f, 2.8f), A(1.3f, 1.8f), A(2f, 2.8f)), mHojas);
            }
            Vector3 alto = Rama(r, copa - Vector3.up * 0.2f, new Vector3(A(-0.4f, 0.4f), 1, A(-0.4f, 0.4f)), 2f, 0.4f);
            if (v == 1) Pieza(PrimitiveType.Sphere, "Hojas", r, alto, new Vector3(3f, 2f, 3f), mHojas);
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 2.3f, 0);
            col.height = 4.6f;
            col.radius = 0.5f;
            pArboles[v] = Guardar(r, "ArbolSeco_" + (v + 1));
        }

        // --- Lápida ---
        {
            var r = new GameObject("Lapida").transform;
            Pieza(PrimitiveType.Cube, "Base", r, new Vector3(0, 0.08f, 0), new Vector3(0.9f, 0.16f, 0.4f), mPiedra);
            Pieza(PrimitiveType.Cube, "Cuerpo", r, new Vector3(0, 0.55f, 0), new Vector3(0.7f, 0.8f, 0.18f), mPiedra);
            Pieza(PrimitiveType.Cylinder, "Arco", r, new Vector3(0, 0.95f, 0), new Vector3(0.7f, 0.09f, 0.7f), mPiedra, new Vector3(90, 0, 0));
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.6f, 0);
            col.size = new Vector3(0.8f, 1.2f, 0.3f);
            pLapida = Guardar(r, "Lapida");
        }

        // --- Cruz ---
        {
            var r = new GameObject("Cruz").transform;
            Pieza(PrimitiveType.Cube, "Vertical", r, new Vector3(0, 0.7f, 0), new Vector3(0.18f, 1.4f, 0.14f), mPiedra);
            Pieza(PrimitiveType.Cube, "Horizontal", r, new Vector3(0, 0.95f, 0), new Vector3(0.75f, 0.18f, 0.14f), mPiedra);
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.7f, 0);
            col.size = new Vector3(0.75f, 1.4f, 0.2f);
            pCruz = Guardar(r, "Cruz");
        }

        // --- Farol ---
        {
            var r = new GameObject("Farol").transform;
            Pieza(PrimitiveType.Cylinder, "Base", r, new Vector3(0, 0.1f, 0), new Vector3(0.4f, 0.1f, 0.4f), mMetal);
            Pieza(PrimitiveType.Cylinder, "Poste", r, new Vector3(0, 1.6f, 0), new Vector3(0.1f, 1.6f, 0.1f), mMetal);
            Pieza(PrimitiveType.Cube, "Lampara", r, new Vector3(0, 3.4f, 0), new Vector3(0.35f, 0.45f, 0.35f), mLuzFarol);
            Pieza(PrimitiveType.Cube, "Techo", r, new Vector3(0, 3.7f, 0), new Vector3(0.5f, 0.1f, 0.5f), mMetal);
            LuzPunto(r, new Vector3(0, 3.3f, 0), new Color(1f, 0.8f, 0.5f), 14f, 6f, false);
            var col = r.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 1.8f, 0);
            col.height = 3.6f;
            col.radius = 0.15f;
            pFarol = Guardar(r, "Farol");
        }

        // --- Cerca de madera (tramo de 2 m) ---
        {
            var r = new GameObject("Cerca").transform;
            for (int i = 0; i < 5; i++)
                Pieza(PrimitiveType.Cube, "Tabla" + i, r, new Vector3(i * 0.45f, 0.55f, 0), new Vector3(0.14f, 1.1f - (i % 2) * 0.15f, 0.05f), mMadera, new Vector3(0, 0, (i % 3 - 1) * 3f));
            Pieza(PrimitiveType.Cube, "Travesano", r, new Vector3(0.9f, 0.65f, 0.04f), new Vector3(2.1f, 0.1f, 0.05f), mMadera);
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0.9f, 0.55f, 0);
            col.size = new Vector3(2.1f, 1.1f, 0.15f);
            pCerca = Guardar(r, "Cerca");
        }

        // --- Murciélago ---
        {
            var r = new GameObject("Murcielago").transform;
            Pieza(PrimitiveType.Sphere, "Cuerpo", r, Vector3.zero, new Vector3(0.2f, 0.2f, 0.3f), mNegro);
            Pieza(PrimitiveType.Cube, "AlaIzq", r, new Vector3(-0.3f, 0.04f, 0), new Vector3(0.5f, 0.02f, 0.25f), mNegro, new Vector3(0, 0, -15));
            Pieza(PrimitiveType.Cube, "AlaDer", r, new Vector3(0.3f, 0.04f, 0), new Vector3(0.5f, 0.02f, 0.25f), mNegro, new Vector3(0, 0, 15));
            r.gameObject.AddComponent<VueloCircular>();
            pMurcielago = Guardar(r, "Murcielago");
        }

        // --- Fantasma ---
        {
            var r = new GameObject("Fantasma").transform;
            Pieza(PrimitiveType.Capsule, "Cuerpo", r, new Vector3(0, 0, 0), new Vector3(0.7f, 0.8f, 0.7f), mFantasma);
            Pieza(PrimitiveType.Sphere, "OjoIzq", r, new Vector3(-0.14f, 0.35f, 0.3f), new Vector3(0.12f, 0.16f, 0.08f), mNegro);
            Pieza(PrimitiveType.Sphere, "OjoDer", r, new Vector3(0.14f, 0.35f, 0.3f), new Vector3(0.12f, 0.16f, 0.08f), mNegro);
            Pieza(PrimitiveType.Sphere, "Boca", r, new Vector3(0, 0.12f, 0.33f), new Vector3(0.12f, 0.18f, 0.08f), mNegro);
            LuzPunto(r, Vector3.zero, new Color(0.6f, 0.9f, 1f), 5f, 1.5f, false);
            var vuelo = r.gameObject.AddComponent<VueloCircular>();
            vuelo.inclinacion = 0f;
            pFantasma = Guardar(r, "Fantasma");
        }

        // --- Brujas: una de pie con su escoba y otra volando ---
        pBruja = CrearBruja(false);
        pBrujaVolando = CrearBruja(true);

        // --- Caldero con poción ---
        {
            var r = new GameObject("Caldero").transform;
            Pieza(PrimitiveType.Sphere, "Olla", r, new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.9f, 1.1f), mMetal);
            Pieza(PrimitiveType.Cylinder, "Borde", r, new Vector3(0, 0.8f, 0), new Vector3(0.95f, 0.05f, 0.95f), mMetal);
            Pieza(PrimitiveType.Cylinder, "Pocion", r, new Vector3(0, 0.84f, 0), new Vector3(0.8f, 0.03f, 0.8f), mPocion);
            LuzPunto(r, new Vector3(0, 1.3f, 0), new Color(0.3f, 1f, 0.3f), 7f, 3f, true);

            var burbujas = new GameObject("Burbujas");
            burbujas.transform.SetParent(r, false);
            burbujas.transform.localPosition = new Vector3(0, 0.9f, 0);
            burbujas.transform.localEulerAngles = new Vector3(-90, 0, 0);
            var ps = burbujas.AddComponent<ParticleSystem>();
            burbujas.GetComponent<ParticleSystemRenderer>().sharedMaterial = mBrillo;
            var main = ps.main;
            main.startLifetime = 1.5f;
            main.startSpeed = 0.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new Color(0.4f, 1f, 0.4f);
            var em = ps.emission; em.rateOverTime = 12;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15; sh.radius = 0.3f;

            var col = r.gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.45f, 0);
            col.radius = 0.55f;
            pCaldero = Guardar(r, "Caldero");
        }
    }

    // ------------------------------------------------------------------
    //  TERRENO
    // ------------------------------------------------------------------

    /// <summary>Camino de tierra que cruza el terreno haciendo curvas: 1 en el camino, 0 fuera.</summary>
    static float Camino(float nx, float nz)
    {
        float centro = 0.5f + 0.12f * Mathf.Sin(nz * Mathf.PI * 2f);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.015f, 0.035f, Mathf.Abs(nx - centro)));
    }

    static float CaminoX(float z) => (0.5f + 0.12f * Mathf.Sin(z / Tam * Mathf.PI * 2f)) * Tam;

    static Terrain CrearTerreno(Texture2D pasto, Texture2D tierra)
    {
        var td = new TerrainData { heightmapResolution = ResAlt };
        td.size = new Vector3(Tam, Alto, Tam);

        // Colinas suaves, más altas en los bordes, y el camino aplanado
        var h = new float[ResAlt, ResAlt];
        for (int z = 0; z < ResAlt; z++)
            for (int x = 0; x < ResAlt; x++)
            {
                float nx = x / (float)(ResAlt - 1), nz = z / (float)(ResAlt - 1);
                float colinas = 0.08f + 0.10f * Mathf.PerlinNoise(nx * 4f + 3f, nz * 4f + 3f) + 0.03f * Mathf.PerlinNoise(nx * 12f, nz * 12f);
                float borde = Mathf.Max(Mathf.Abs(nx - 0.5f), Mathf.Abs(nz - 0.5f)) * 2f;
                colinas += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, borde)) * 0.5f;
                h[z, x] = Mathf.Lerp(colinas, colinas * 0.85f, Camino(nx, nz));
            }
        td.SetHeights(0, 0, h);

        // Dos texturas: pasto y tierra (en el camino y en manchas)
        td.terrainLayers = new[] { Capa("Pasto", pasto, 6f), Capa("Tierra", tierra, 5f) };
        td.alphamapResolution = ResMapa;
        var a = new float[ResMapa, ResMapa, 2];
        for (int z = 0; z < ResMapa; z++)
            for (int x = 0; x < ResMapa; x++)
            {
                float nx = (x + 0.5f) / ResMapa, nz = (z + 0.5f) / ResMapa;
                float manchas = Mathf.InverseLerp(0.65f, 0.8f, Mathf.PerlinNoise(nx * 10f + 50f, nz * 10f + 50f));
                float t = Mathf.Clamp01(Mathf.Max(Camino(nx, nz), manchas * 0.7f));
                a[z, x, 0] = 1f - t;
                a[z, x, 1] = t;
            }
        td.SetAlphamaps(0, 0, a);
        AssetDatabase.CreateAsset(td, Raiz + "/Terreno/TerrenoHalloween.asset");

        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terreno";
        var ter = go.GetComponent<Terrain>();
        var matBase = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat");
        if (matBase != null) ter.materialTemplate = matBase;
        return ter;
    }

    static TerrainLayer Capa(string nombre, Texture2D tex, float tam)
    {
        var l = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tam, tam), smoothness = 0f };
        AssetDatabase.CreateAsset(l, $"{Raiz}/Terreno/{nombre}.terrainlayer");
        return l;
    }

    // ------------------------------------------------------------------
    //  AMBIENTE (noche, luna, niebla)
    // ------------------------------------------------------------------

    static void CrearAmbiente()
    {
        var amb = new GameObject("Ambiente").transform;

        var luzLuna = new GameObject("Luz de Luna");
        luzLuna.transform.SetParent(amb, false);
        luzLuna.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
        var luz = luzLuna.AddComponent<Light>();
        luz.type = LightType.Directional;
        luz.color = new Color(0.45f, 0.7f, 0.85f);
        luz.intensity = 0.6f;
        luz.shadows = LightShadows.Soft;

        // La luna: una esfera brillante lejos, en la dirección de donde viene la luz
        var luna = Pieza(PrimitiveType.Sphere, "Luna", amb, new Vector3(Tam / 2f, 0, Tam / 2f) - luzLuna.transform.forward * 160f, Vector3.one * 18f, mLuna);
        luna.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        RenderSettings.sun = luz;
        RenderSettings.skybox = mCielo;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.10f, 0.25f, 0.30f);
        RenderSettings.ambientEquatorColor = new Color(0.07f, 0.17f, 0.20f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.07f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.05f, 0.20f, 0.24f);
        RenderSettings.fogDensity = 0.012f;

        // Post-procesado: brillo en las luces y bordes oscuros
        var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(perfil, Raiz + "/Materiales/PostProcesado.asset");
        var bloom = perfil.Add<Bloom>(true);
        bloom.intensity.Override(1.2f);
        bloom.threshold.Override(0.9f);
        var vin = perfil.Add<Vignette>(true);
        vin.intensity.Override(0.4f);
        foreach (var comp in perfil.components)
        {
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(comp, perfil);
        }
        EditorUtility.SetDirty(perfil);
        var vol = new GameObject("PostProcesado").AddComponent<Volume>();
        vol.transform.SetParent(amb, false);
        vol.isGlobal = true;
        vol.sharedProfile = perfil;
    }

    // ------------------------------------------------------------------
    //  DECORACIÓN
    // ------------------------------------------------------------------

    static float Suelo(Terrain ter, float x, float z) => ter.SampleHeight(new Vector3(x, 0, z));

    static GameObject Poner(GameObject prefab, Transform padre, Terrain ter, float x, float z, float giro, float escala = 1f, float altura = 0f)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        go.transform.SetPositionAndRotation(new Vector3(x, Suelo(ter, x, z) + altura, z), Quaternion.Euler(0, giro, 0));
        go.transform.localScale = Vector3.one * escala;
        return go;
    }

    static void Decorar(Terrain ter)
    {
        var cementerio = new Vector2(70f, 105f); // centro del cementerio

        // Faroles a los lados del camino
        var gFaroles = new GameObject("Faroles").transform;
        for (int i = 0; i < 7; i++)
        {
            float z = 35f + i * 22f;
            float lado = i % 2 == 0 ? 4.5f : -4.5f;
            Poner(pFarol, gFaroles, ter, CaminoX(z) + lado, z, 0f);
        }

        // Árboles secos repartidos (lejos del camino y del cementerio)
        var gArboles = new GameObject("Arboles").transform;
        int puestos = 0;
        for (int i = 0; i < 400 && puestos < 55; i++)
        {
            float x = R(15f, Tam - 15f), z = R(15f, Tam - 15f);
            if (Mathf.Abs(x - CaminoX(z)) < 6f) continue;
            if (Vector2.Distance(new Vector2(x, z), cementerio) < 16f) continue;
            Poner(pArboles[rnd.Next(2)], gArboles, ter, x, z, R(0, 360), R(0.8f, 1.6f), -0.2f);
            puestos++;
        }

        // Calabazas: al borde del camino y sueltas por el bosque
        var gCalabazas = new GameObject("Calabazas").transform;
        for (int i = 0; i < 16; i++)
        {
            float z = 30f + i * 9.5f;
            float lado = (i % 2 == 0 ? 1 : -1) * R(3.2f, 4.2f);
            float x = CaminoX(z) + lado;
            // Mirando hacia el camino
            Poner(pCalabaza, gCalabazas, ter, x, z, lado > 0 ? -90f + R(-25, 25) : 90f + R(-25, 25), R(0.7f, 1.3f));
        }
        for (int i = 0; i < 14; i++)
        {
            float x = R(25f, Tam - 25f), z = R(25f, Tam - 25f);
            if (Mathf.Abs(x - CaminoX(z)) < 5f) continue;
            Poner(pCalabaza, gCalabazas, ter, x, z, R(0, 360), R(0.6f, 1.5f));
        }

        // Cementerio: lápidas en filas, rodeado por una cerca
        var gCementerio = new GameObject("Cementerio").transform;
        for (int fila = 0; fila < 4; fila++)
            for (int col = 0; col < 5; col++)
            {
                float x = cementerio.x - 8f + col * 4f + R(-0.4f, 0.4f);
                float z = cementerio.y - 6f + fila * 4f + R(-0.4f, 0.4f);
                var pf = rnd.Next(4) == 0 ? pCruz : pLapida;
                var lapida = Poner(pf, gCementerio, ter, x, z, 90f + R(-10, 10), R(0.9f, 1.2f), -0.05f);
                lapida.transform.Rotate(R(-6, 6), 0, R(-8, 8)); // un poco torcidas
            }
        for (int i = 0; i < 12; i++)
        {
            // lados de arriba y abajo
            Poner(pCerca, gCementerio, ter, cementerio.x - 12f + i * 2f, cementerio.y - 10f, 0f);
            Poner(pCerca, gCementerio, ter, cementerio.x - 12f + i * 2f, cementerio.y + 10f, 0f);
        }
        for (int i = 0; i < 10; i++)
        {
            // lado del fondo (el lado del camino queda abierto como entrada)
            Poner(pCerca, gCementerio, ter, cementerio.x - 12f, cementerio.y - 10f + i * 2f, -90f);
        }

        // Fantasmas flotando en el cementerio
        var gFantasmas = new GameObject("Fantasmas").transform;
        for (int i = 0; i < 3; i++)
        {
            var f = (GameObject)PrefabUtility.InstantiatePrefab(pFantasma, gFantasmas);
            var vuelo = f.GetComponent<VueloCircular>();
            float x = cementerio.x - 5f + i * 5f, z = cementerio.y - 3f + i * 3f;
            vuelo.centro = new Vector3(x, Suelo(ter, x, z) + 2.2f, z);
            vuelo.radio = 2f + i;
            vuelo.velocidad = 0.4f;
            vuelo.fase = i * 2f;
            vuelo.Colocar(0f);
        }

        // Murciélagos volando en círculos
        var gMurcielagos = new GameObject("Murcielagos").transform;
        for (int i = 0; i < 6; i++)
        {
            var m = (GameObject)PrefabUtility.InstantiatePrefab(pMurcielago, gMurcielagos);
            m.transform.localScale = Vector3.one * 1.5f;
            var vuelo = m.GetComponent<VueloCircular>();
            float z = 60f + i * 15f;
            vuelo.centro = new Vector3(CaminoX(z), Suelo(ter, CaminoX(z), z) + 7f + i, z);
            vuelo.radio = 6f + i * 2f;
            vuelo.velocidad = 0.8f + i * 0.1f;
            vuelo.fase = i * 1.3f;
            vuelo.Colocar(0f);
        }

        // Caldero de bruja junto al inicio del camino
        float cz = 48f, cx = CaminoX(cz) + 7f;
        var gBruja = new GameObject("Rincon de la Bruja").transform;
        Poner(pCaldero, gBruja, ter, cx, cz, 0f);
        Poner(pCalabaza, gBruja, ter, cx + 1.5f, cz + 0.5f, -120f, 0.8f);
        Poner(pCalabaza, gBruja, ter, cx - 1.2f, cz + 1.2f, -200f, 0.6f);

        // Brujas: una removiendo el caldero, otra en la entrada del cementerio y dos volando
        var gBrujas = new GameObject("Brujas").transform;
        Poner(pBruja, gBrujas, ter, cx, cz - 1.5f, 0f);
        Poner(pBruja, gBrujas, ter, cementerio.x + 14f, cementerio.y + 3f, 90f);
        for (int i = 0; i < 2; i++)
        {
            var b = (GameObject)PrefabUtility.InstantiatePrefab(pBrujaVolando, gBrujas);
            var vuelo = b.GetComponent<VueloCircular>();
            float z = 90f + i * 50f;
            vuelo.centro = new Vector3(CaminoX(z), Suelo(ter, CaminoX(z), z) + 12f + i * 5f, z);
            vuelo.radio = 14f + i * 8f;
            vuelo.velocidad = 0.35f - i * 0.1f;
            vuelo.fase = i * 3f;
            vuelo.Colocar(0f);
        }
    }

    // ------------------------------------------------------------------
    //  JUGADOR
    // ------------------------------------------------------------------

    static Camera CrearJugador(Terrain ter)
    {
        float z = 28f, x = CaminoX(z);
        var j = new GameObject("Jugador");
        j.transform.position = new Vector3(x, Suelo(ter, x, z) + 0.1f, z);
        var cc = j.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.center = new Vector3(0, 0.9f, 0);

        var camGo = new GameObject("Camara");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(j.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = 800f;
        camGo.AddComponent<AudioListener>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        j.AddComponent<PrimeraPersona>().camara = camGo.transform;

        // Luciérnagas alrededor del jugador
        var luc = new GameObject("Luciernagas");
        luc.transform.SetParent(j.transform, false);
        luc.transform.localPosition = new Vector3(0, 2f, 0);
        var ps = luc.AddComponent<ParticleSystem>();
        luc.GetComponent<ParticleSystemRenderer>().sharedMaterial = mBrillo;
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = 0.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new Color(0.8f, 1f, 0.4f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var em = ps.emission; em.rateOverTime = 25;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 4f, 40f);
        var ruido = ps.noise; ruido.enabled = true; ruido.strength = 0.6f; ruido.frequency = 0.4f;

        // Niebla baja
        var nie = new GameObject("NieblaBaja");
        nie.transform.SetParent(j.transform, false);
        nie.transform.localPosition = new Vector3(0, 0.5f, 0);
        var pn = nie.AddComponent<ParticleSystem>();
        nie.GetComponent<ParticleSystemRenderer>().sharedMaterial = mNiebla;
        var mn = pn.main;
        mn.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
        mn.startSpeed = 0.3f;
        mn.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        mn.startColor = new Color(0.5f, 0.8f, 0.85f, 0.06f);
        mn.simulationSpace = ParticleSystemSimulationSpace.World;
        mn.maxParticles = 150;
        mn.prewarm = true;
        var en = pn.emission; en.rateOverTime = 12;
        var sn = pn.shape; sn.shapeType = ParticleSystemShapeType.Box; sn.scale = new Vector3(60f, 0.5f, 60f);

        return cam;
    }
}
