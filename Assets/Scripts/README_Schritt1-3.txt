SEGELPHYSIK - Schritt 1-3 (06.10.2026)
=====================================
Dateien:
  WaterBody.cs              -> auf Objekt "Water" (oder neues Empty) legen
  BuoyancyMesh.cs           -> auf "Yacht" (Empty mit Rigidbody) legen
  ForceArrowVisualizer.cs   -> auf "Yacht" legen (Pfeile entstehen als Szene-Objekte)

INSPEKTOR-ANPASSUNGEN (nach jeder Skript-Aenderung pruefen! Unity-Falle:
neue [SerializeField]-Felder bekommen NICHT die Code-Defaults, sondern 0/alte Werte)

1) WaterBody (Objekt "Water")
   - density          = 1025   (Seewasser; Presets: 1000 Suess / 1025 See / ~1240 Totes Meer / 13546 Quecksilber)
   - surfaceHeight    = 0
   - gizmoSize        = 20 (nur Darstellung)

2) BuoyancyMesh (auf Yacht)
   - waterBody        = Water (Referenz)
   - meshFilter       = Rumpf (Kind der Yacht; wird sonst automatisch im Kind gesucht)
   - additionalMeshes = Groesse 3: Kiel, Bulb, Ruder   (NICHT Mast/Boom/Segel!)
   - gravity          = 9.81
   - waterDamping     = 1.5   (Startwert; 0 = aus)
   - rotationDamping  = 2.5   (Startwert; 0 = aus)
   - showHUD          = an (zeigt V_nass)

3) ForceArrowVisualizer (auf Yacht)
   - targetRigidbody  = Yacht (Rigidbody)
   - buoyancy         = BuoyancyMesh (Yacht)
   - newtonPerMeter   = 4000   (m·g ~ 115 kN -> ~29-m-Pfeil; kleiner = laenger)
   - showLeverArm     = an
   - tipSize          = 1.5 / shaftWidth = 0.15

4) Yacht-Rigidbody (bleibt wie gehabt)
   - Mass 11700 · Angular Damping 3 · CoM (0, -2.5, -0.3) · Continuous · Interpolate
   - Hinweis: Angular Damping 3 ist hoch - falls das Boot "schwammig" wirkt, auf 1-2 senken.

5) Szene aufraeumen (Schritt 4)
   - Ocean: OceanWaves-Skript entfernen, KEIN MeshCollider (Wasser rein visuell)
   - YachtBuoyancy.cs von Yacht entfernen (Datei behalten)
   - Buoyancy.cs + HullGenerator.cs loeschen
   - WuerfelTest-Objekt loeschen

VERIFIKATION
   - Play: Boot sollte ruhig auf y ~ 0 liegen
   - HUD: V_nass ~ 11,4 m³ (11700/1025) -> sonst Wasserlinie falsch
   - Pfeile: rot (CoG) + blau (CoB) liegen im Gleichgewicht auf einer Vertikalen, gleich lang
   - Beim Kraengen wandert CoB seitlich -> sichtbares Aufrichtmoment (GZ-Pfeil)
