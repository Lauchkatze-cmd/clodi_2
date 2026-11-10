# Game Prototype 1 – Checkliste

Nov 10, 2026 · @Elli

Abgabe: Nov 16, 2026, 24:00 über Moodle. Abgehakt ist, was laut GitHub-Stand (Commit vom 9.11.) schon erledigt ist.

## Level Design (15 Punkte)

- [ ] ProBuilder-Objekte im Level (Brücke, Turm, Hütte, Nest, Trittsteine)
- [ ] Terrain mit Hügeln, Graben und Texturen
- [ ] Mindestens ein spielbares Level
- [ ] Terrain von Hand mit den Terrain-Pinseln nachbearbeiten (Hügel, Weg, Gras oder Bäume)
- [ ] 2–3 ProBuilder-Objekte selbst bauen oder umformen (z. B. Steg mit Extrude)
- [ ] Layout durchspielen: Sind alle 5 Eier erreichbar?
- [ ] Layout durchspielen: Ist der Weg zum Nest klar erkennbar?

## Game Logic (20 Punkte)

- [ ] Spielfigur mit Steuerung (Laufen, Springen)
- [ ] Klares Ziel: 5 Eier sammeln und ins Nest bringen
- [ ] Mechaniken: Eier sammeln, Matschkugel werfen, Leben
- [ ] Interaktion mit der Welt (Eier, Grube, Nest, Gegner betäuben)
- [ ] Gewinnen (Nest) und Verlieren (Grube oder 0 Leben)
- [ ] Sprint-Fähigkeit mit Ausdauer
- [ ] Zielhilfe für die Matschkugel
- [ ] Neues Clodi-Modell im Spiel testen: braun, nicht durchsichtig, richtige Blickrichtung
- [ ] Schritt-Animation testen: keine rutschenden Füße
- [ ] Sprint und Zielen im Spiel testen
- [ ] Einmal komplett gewinnen und einmal verlieren

## Künstliche Intelligenz (15 Punkte)

- [ ] NavMesh für die Wegfindung
- [ ] NavMesh Agent auf den Gegnern
- [ ] Behavior Tree steuert die Entscheidungen
- [ ] Mindestens 3 Verhaltensweisen (Patrol, Chase, Attack, Search, Stunned)
- [ ] Beim Lehrer nachfragen: Ist ein Behavior Tree in C# okay, oder muss es das Unity-Behavior-Paket sein?
- [ ] Gegner testen: Patrouillieren, Verfolgen, Angreifen, Suchen, Betäubt werden
- [ ] Gegner etwas fairer machen (kurz ausholen vor dem Angriff)

## Allgemeine Anforderungen

- [ ] Engine Unity
- [ ] Plattform PC
- [ ] Der ganze Code ist auf Englisch (Namen und Kommentare)
- [ ] Team klären: allein oder höchstens 3 Personen
- [ ] Alle Tools funktionieren auch im fertigen Build
- [ ] Feinschliff für „besser als frühere Jahrgänge“: Startmenü, Sound, Lichtstimmung
- [ ] Jedes Skript in groben Zügen erklären können
- [ ] Klären, ob KI-Hilfe angegeben werden muss

## Abgabe

- [ ] Projekt liegt auf GitHub (`Lauchkatze-cmd/clodi_2`)
- [ ] Repository für den Lehrer zugänglich (öffentlich oder als Collaborator eingeladen)
- [ ] PC-Build erstellen: File → Build Profiles → Windows → Build, in den Ordner `Build/`
- [ ] Build ohne Unity starten und durchspielen
- [ ] `Repository.txt` mit dem GitHub-Link anlegen
- [ ] `TeamMembers.txt` mit allen vollen Namen anlegen
- [ ] ZIP benennen: `[Volle Namen]-[Projektname].zip`
- [ ] ZIP auf Moodle hochladen, am besten am Vormittag des 16.11.
