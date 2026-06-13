# Der FactoryPlanner – verständlich erklärt

> Ziel dieses Dokuments: Du sollst am Ende **verstehen**, was der Auto-Planer tut,
> *warum* er so gebaut ist und *welche Mathematik* dahintersteckt. Die Mathematik
> wird von Grund auf aufgebaut – du brauchst nur Schulwissen (Gleichungen,
> Brüche, Prozent). Jeder Abschnitt verbindet die Idee mit dem echten Code.

Inhalt:

1. [Was macht der Planer überhaupt?](#1-was-macht-der-planer-überhaupt)
2. [Die Grundidee in einem Satz](#2-die-grundidee-in-einem-satz)
3. [Mini-Beispiel von Hand: 30 Eisenplatten/min](#3-mini-beispiel-von-hand-30-eisenplattenmin)
4. [Warum überhaupt „optimieren"?](#4-warum-überhaupt-optimieren)
5. [Was ist Lineare Optimierung (LP)?](#5-was-ist-lineare-optimierung-lp)
6. [Wie der Code das Problem aufstellt](#6-wie-der-code-das-problem-aufstellt)
7. [Das Simplex-Verfahren: wie gelöst wird](#7-das-simplex-verfahren-wie-gelöst-wird)
8. [Warum exakte Brüche statt Kommazahlen?](#8-warum-exakte-brüche-statt-kommazahlen)
9. [Warum eine „dünn besetzte" Matrix?](#9-warum-eine-dünn-besetzte-matrix)
10. [Die zwei Stufen: Maximieren & „mehr habe ich nicht"](#10-die-zwei-stufen-maximieren--mehr-habe-ich-nicht)
11. [Vom Rechenergebnis zur fertigen Fabrik](#11-vom-rechenergebnis-zur-fertigen-fabrik)
12. [Landkarte: welche Datei macht was](#12-landkarte-welche-datei-macht-was)
13. [Glossar](#13-glossar)

---

## 1. Was macht der Planer überhaupt?

Du sagst dem Planer **was du haben willst**, z. B. „900 Plastik pro Minute". Er
antwortet mit einer **kompletten Fabrik**:

- *welche* Rezepte gebaut werden müssen,
- *wie viele* Maschinen von jedem (auch krumme Zahlen wie 1,5 sind erlaubt),
- *welche* Rohstoffe in welcher Menge reinfließen,
- *welche* Nebenprodukte (Abfall) entstehen,
- und das alles **optimal** nach einem Ziel deiner Wahl (möglichst wenig
  Rohstoffe / Strom / Maschinen).

Das Beeindruckende: Er findet sogar **Recycling-Kreisläufe** von alleine – z. B.
die bekannte „Zero-Waste"-Plastikkette, bei der Nebenprodukte über
Alternativrezepte wieder in die Kette zurückgeführt werden, sodass am Ende
**kein Abfall** übrig bleibt. Genau dieser Fall steht als Test im Projekt:

```csharp
// Ficsit.Schematics.Tests/FactoryPlannerTests.cs
request.Targets.Add(new PlanTarget("Plastic", new Rational(900)));
var plan = FactoryPlanner.Plan(TestData.Database, request);

Assert.Equal(new Rational(300), plan.Supplies["Crude Oil"]); // genau 300 Öl rein
Assert.Empty(plan.Sinks);                                     // null Abfall
```

Der Knackpunkt für das Verständnis: Der Planer „bastelt" nicht herum und probiert
auch nicht stur den Rezeptbaum durch. Er übersetzt deine Anfrage in ein
**mathematisches Problem** und löst es exakt. Den Rest dieses Dokuments verbringen
wir damit, genau dieses Problem zu verstehen.

---

## 2. Die Grundidee in einem Satz

> Der Planer formuliert deine Anfrage als **lineares Optimierungsproblem** und
> löst es mit dem **Simplex-Verfahren** – exakt, in Brüchen, ohne Rundungsfehler.

Diese eine Idee zieht sich durch alles. „Linear" und „Simplex" klären wir gleich.
Vorher rechnen wir ein winziges Beispiel **von Hand**, damit du die Begriffe an
echten Zahlen festmachen kannst.

---

## 3. Mini-Beispiel von Hand: 30 Eisenplatten/min

Vergiss kurz den ganzen Code. Wir wollen **30 Eisenplatten pro Minute**.
Im Spiel gibt es dafür (im Basisspiel) zwei Schritte:

| Rezept | Maschine | rein | raus |
|---|---|---|---|
| Iron Ingot | Schmelze | 30 Eisenerz/min | 30 Eisenbarren/min |
| Iron Plate | Konstruktor | 30 Eisenbarren/min | 20 Eisenplatten/min |

### Schritt 1: Was sind unsere Unbekannten?

Wir wissen nicht, wie viele Maschinen wir brauchen. Das sind unsere **Variablen**
(unsere „Stellschrauben"). Nennen wir sie:

- `x₁` = Anzahl der **Schmelzen** (Eisenbarren)
- `x₂` = Anzahl der **Konstruktoren** (Eisenplatten)
- `s` = Menge **Eisenerz**, die wir von außen reinkippen (pro min)

Diese Zahlen dürfen krumm sein (1,5 Maschinen = eine Maschine auf 150 % Takt oder
zwei Maschinen, die sich die Arbeit teilen) und sie dürfen **nie negativ** sein
(man kann keine –2 Maschinen bauen). Das ist eine wichtige Spielregel: alle
Variablen sind `≥ 0`.

### Schritt 2: Gleichungen aufstellen („Erhaltungssatz")

Für **jeden Stoff** gilt eine einfache Buchhaltungsregel:

> Was hereinkommt, muss herausgehen. Nichts entsteht aus dem Nichts, nichts
> verschwindet im Nichts.

Schauen wir Stoff für Stoff:

**Eisenplatten** (das wollen wir – 30/min sollen die Fabrik *verlassen*):
Nur der Konstruktor produziert sie, jeder macht 20/min.

$$20 \cdot x_2 = 30 \quad\Rightarrow\quad x_2 = 1{,}5$$

**Eisenbarren** (Zwischenprodukt – soll *komplett* weiterverarbeitet werden, nichts
bleibt übrig):
Schmelzen produzieren `30·x₁`, Konstruktoren verbrauchen `30·x₂`.

$$\underbrace{30 \cdot x_1}_{\text{produziert}} - \underbrace{30 \cdot x_2}_{\text{verbraucht}} = 0 \quad\Rightarrow\quad x_1 = x_2 = 1{,}5$$

**Eisenerz** (Rohstoff – wir kippen `s` rein, Schmelzen verbrauchen `30·x₁`):

$$\underbrace{s}_{\text{rein}} - \underbrace{30 \cdot x_1}_{\text{verbraucht}} = 0 \quad\Rightarrow\quad s = 45$$

### Ergebnis

> **1,5 Schmelzen + 1,5 Konstruktoren + 45 Eisenerz/min → 30 Eisenplatten/min.**

Das war's. Drei Variablen, drei Gleichungen (eine pro Stoff), aufgelöst. **Genau
diese Buchhaltung macht der Planer** – nur eben mit hunderten Stoffen und Rezepten
gleichzeitig.

Drei Begriffe halten wir fest, weil sie im Code genau so wiederkommen:

- **Variablen** = Maschinenzahlen `x₁, x₂` und Außenzufuhren `s`. → im Code „**Spalten**".
- **Eine Gleichung pro Stoff** (rein = raus). → im Code „**Bilanzzeilen**" (rows).
- **Die rechte Seite** der Gleichung: meistens `0` (Zwischenprodukt), beim
  Zielprodukt der gewünschte Wert (`30`). → im Code der **Vektor `b`**.

---

## 4. Warum überhaupt „optimieren"?

Im Beispiel oben gab es **nur einen Weg**, also genau eine Lösung – nichts zu
optimieren. Aber Satisfactory hat **Alternativrezepte**. Sobald es *mehr als einen
Weg* gibt, etwas herzustellen, gibt es eine **Auswahl** – und damit ein „bestes".

Beispiel: Eisenbarren kann man auch mit dem Alternativrezept *„Pure Iron Ingot"*
herstellen (Eisenerz **+ Wasser** → mehr Barren pro Erz). Jetzt gilt:

- Variante A (normal): 30 Erz → 30 Barren
- Variante B (alternativ): braucht zwar Wasser, ist aber **erz-sparender**

Welche soll der Planer nehmen? Das hängt von deinem **Ziel** ab:

- „Wenig Rohstoffe" → Variante B (spart das knappe Erz, Wasser ist quasi gratis).
- „Wenig Maschinen" → vielleicht Variante A.

Jetzt haben wir nicht mehr *eine* Lösung, sondern **unendlich viele gültige
Fabriken**, und suchen die **günstigste** nach einem Maßstab. Das ist
**Optimierung**. Und weil alle Beziehungen linear sind (doppelt so viele
Maschinen = doppelt so viel Output, keine `x²`-Effekte), ist es **lineare**
Optimierung.

Der Maßstab („wonach ist günstig?") wird im Code gewählt:

```csharp
// Ficsit.Schematics.Core/Planning/PlanBias.cs
public enum PlanBias
{
    Resources, // möglichst wenig Rohstoffe (nach Seltenheit gewichtet)
    Power,     // möglichst wenig Strom
    Machines,  // möglichst wenige Maschinen
}
```

---

## 5. Was ist Lineare Optimierung (LP)?

„LP" steht für **L**inear **P**rogramming. Drei Zutaten:

1. **Variablen**, die wir frei wählen (hier: Maschinenzahlen, Außenzufuhren),
   alle `≥ 0`.
2. **Nebenbedingungen** (Constraints): lineare Gleichungen/Ungleichungen, die
   erfüllt sein *müssen* (die Stoffbilanzen aus Abschnitt 3, plus Obergrenzen wie
   „höchstens 60 Erz/min verfügbar").
3. Eine **Zielfunktion**: eine lineare Formel, die wir so **klein wie möglich**
   machen wollen (z. B. „Summe der gewichteten Rohstoffe").

### Das Bild dazu (anschaulich mit 2 Variablen)

Stell dir nur zwei Variablen vor, `x` und `y`, als Punkt in einem Koordinatensystem.

- Jede Ungleichung („`x ≥ 0`", „`x + 2y ≤ 60`", …) teilt die Ebene in „erlaubt"
  und „verboten" – sie schneidet eine Halbebene weg.
- Alle Bedingungen zusammen lassen ein **Vieleck** (Polygon) übrig: die Menge
  **aller erlaubten Kombinationen**. Man nennt das den **zulässigen Bereich**.

```
   y
   │      zulässiger Bereich (Vieleck)
   │        ____________
   │       /            \      ← jede Kante ist eine Nebenbedingung
   │      /   erlaubt     \
   │     /                 •  ← die optimale Lösung sitzt in einer ECKE
   │     \                /
   │      \______________/
   └───────────────────────── x
```

- Die Zielfunktion („mache die Kosten minimal") ist wie eine **Richtung**, in die
  man im Bild schiebt. Der beste Punkt liegt **immer in einer Ecke** des Vielecks.

Das ist der zentrale Satz der linearen Optimierung – und er gilt auch in vielen
Dimensionen (bei uns hat das „Vieleck" so viele Dimensionen wie es Variablen gibt,
also leicht ein paar hundert). Die Wahrheit bleibt: **eine optimale Lösung steckt
immer in einer Ecke.**

Warum ist das so wichtig? Weil es unendlich viele Punkte im Inneren gibt, aber nur
**endlich viele Ecken**. Wir müssen also nicht „alles" durchsuchen, sondern nur
die Ecken. Genau das macht das Simplex-Verfahren.

---

## 6. Wie der Code das Problem aufstellt

Der gesamte Aufbau passiert in
[`FactoryPlanner.Plan(...)`](../Ficsit.Schematics.Core/Planning/FactoryPlanner.cs).
Der Kommentar oben in der Datei fasst das ganze Vorgehen zusammen:

```csharp
// Ficsit.Schematics.Core/Planning/FactoryPlanner.cs
/// Synthesizes a factory for the requested outputs as an exact linear program:
/// one variable per candidate recipe (machine count), per external supply and
/// per sinkable byproduct, with a balance row per part.
```

Auf Deutsch, Stück für Stück:

### 6.1 Welche Rezepte kommen überhaupt in Frage?

Es wäre verschwenderisch, *alle* Rezepte des Spiels in die Rechnung zu werfen. Der
Planer sammelt nur die, die für dein Ziel relevant sein *können*. Dazu geht er vom
Ziel **rückwärts**: „Was produziert Plastik? → was die *dafür* nötigen Stoffe? →
…" – bis nichts Neues mehr dazukommt. Das ist eine **Breitensuche** rückwärts durch
den Rezeptbaum (`CollectCandidateRecipes`).

```csharp
// Ficsit.Schematics.Core/Planning/FactoryPlanner.cs  (CollectCandidateRecipes)
foreach (var target in request.Targets) Seed(target.Part); // starte bei den Zielen
// ...
void Expand(string part, List<string> buffer)
{
    if (!producers.TryGetValue(part, out var producingRecipes)) return;
    foreach (var recipe in producingRecipes)        // jedes Rezept, das 'part' herstellt
    {
        if (!included.TryAdd(recipe.Name, 0)) continue;
        collected[recipe.Name] = recipe;
        foreach (var reference in recipe.Parts)     // dessen Zutaten als nächstes ansehen
            if (seenParts.TryAdd(reference.Part, 0))
                buffer.Add(reference.Part);
    }
}
```

Wichtig: Es werden auch **Produzenten von Nebenprodukten** mitgenommen. Nur so
können sich **Kreisläufe schließen** (z. B. „Heavy Oil Residue" wieder einkochen)
– der Grund, warum Zero-Waste-Ketten überhaupt gefunden werden.

> Die Parallelisierung (mehrere Threads, `ConcurrentDictionary`, „level-synchronous
> BFS") ist reine **Geschwindigkeitsoptimierung**. Für das Verständnis darfst du
> sie ignorieren: das Ergebnis ist exakt dasselbe wie bei einer simplen Schleife.

### 6.2 Die „Spalten" – das sind die Variablen

Jede Variable wird im Code als **Spalte** angelegt. Es gibt fünf Sorten, markiert
mit einem Buchstaben (`columnKinds`):

| Code | Sorte | Bedeutung (= unsere Variable) |
|---|---|---|
| `r` | **recipe** | Wie viele Maschinen dieses Rezepts? (`x₁, x₂, …`) |
| `s` | **supply** | Wie viel Rohstoff/Vorprodukt kippen wir von außen rein? (`s`) |
| `k` | **sink** (k = Senke) | Wie viel Nebenprodukt werfen wir in den AWESOME Sink? |
| `t` | **bundle** | Hilfsvariable beim Maximieren (Abschnitt 10) |
| `l` | **slack** (Schlupf) | Hilfsvariable für Obergrenzen (siehe unten) |

Die **Rezept-Spalten** entstehen so – pro Stoff im Rezept ein Eintrag „Zeile +
Menge pro Minute":

```csharp
// Ficsit.Schematics.Core/Planning/FactoryPlanner.cs
foreach (var recipe in recipes)
{
    var columnEntries = new List<(int Row, Rational Coefficient)>();
    foreach (var part in recipe.Parts)
        if (partRow.TryGetValue(part.Part, out var row))
            columnEntries.Add((row, recipe.RatePerMinute(part.Part)));
    columns.Add(columnEntries.ToArray());
    columnKinds.Add(('r', recipe.Name));
    costs.Add(RecipeCost(data, recipe, request.Bias)); // Kosten dieser Variable
}
```

`RatePerMinute` ist dabei **positiv für Outputs, negativ für Inputs** – genau die
Vorzeichen aus unserem Handbeispiel:

```csharp
// Ficsit.Schematics.Core/GameData/RecipeDefinition.cs
/// Parts per minute for one machine at 100% clock (positive = output, negative = input).
public Rational RatePerMinute(string part)
{
    var entry = Parts.FirstOrDefault(p => p.Part == part);
    return entry is null ? Rational.Zero : entry.AmountValue * 60 / BatchTimeValue;
}
```

Die **Supply-Spalten** (Außenzufuhr) haben einfach `+1` in der Zeile ihres Stoffs.
Wenn es eine Obergrenze gibt („höchstens 60/min"), bekommt die Spalte zusätzlich
einen Eintrag in einer **Cap-Zeile** (cap = Deckel):

```csharp
if (provided) // der Nutzer hat eine Obergrenze angegeben
{
    columns.Add([(partRow[part], Rational.One), (capRowBase + capRows.Count, Rational.One)]);
    capRows.Add((part, provision.Cap));
}
else
    columns.Add([(partRow[part], Rational.One)]); // unbegrenzt verfügbarer Rohstoff
```

Die **Sink-Spalten** haben `-1` (sie nehmen einen Stoff aus dem System), und sie
sind absichtlich **teuer**, damit der Planer sie meidet und lieber recycelt:

```csharp
var sinkPenalty = request.Byproducts == ByproductMode.Eliminate
    ? SinkPenaltyEliminate   // = 1 000 000  → „bloß nicht wegwerfen, recycle!"
    : SinkPenaltyAllowed;    // = 1/1 000 000 → „wegwerfen ist ok"
```

Das ist ein schöner Trick: Statt zwei verschiedene Algorithmen für „Zero Waste"
und „Abfall erlaubt" zu schreiben, ändert man nur **einen Preis**. Bei
`Eliminate` ist Wegwerfen so absurd teuer, dass die Optimierung von selbst jeden
Recyclingweg vorzieht.

### 6.3 Die „Zeilen" – das sind die Bilanzgleichungen

Es gibt **eine Zeile pro Stoff** (`partRow`), plus die Cap-Zeilen für Obergrenzen.
Jede Stoff-Zeile sagt:

> (alles, was Rezepte + Zufuhren von diesem Stoff erzeugen)
> − (alles, was Rezepte + Senken verbrauchen)  =  rechte Seite `b`

Die **rechte Seite `b`** ist `0` für Zwischenprodukte (rein = raus) und der
**Zielwert** für Zielprodukte:

```csharp
// Ficsit.Schematics.Core/Planning/FactoryPlanner.cs
var b = new Rational[rowCount];
Array.Fill(b, Rational.Zero);                       // Standard: alles ausgeglichen
if (!hasBundle)
    foreach (var (part, rate) in targetByPart)
        b[partRow[part]] = rate;                    // Zielprodukt: netto = gewünschte Rate
for (var i = 0; i < capRows.Count; i++)
    b[capRowBase + i] = capRows[i].Cap;             // Obergrenzen
```

Das ist **identisch** zu unserem Handbeispiel – nur in Tabellenform für alle Stoffe
gleichzeitig.

#### Was ist „slack" (Schlupf)?

Eine Obergrenze ist eine **Ungleichung**: `verbrauchtes Erz ≤ 60`. Der Simplex
mag aber lieber **Gleichungen**. Trick: Man addiert eine künstliche „Rest"-Variable
`l ≥ 0`, die den ungenutzten Rest aufnimmt:

$$\text{verbrauchtes Erz} + l = 60$$

Ist `l = 15`, wurden 45 verbraucht. Ist `l = 0`, ist die Grenze voll ausgereizt
(„Flaschenhals"). Genau dieses `l = 0` liest der Planer am Ende aus, um dir
**Bottlenecks** anzuzeigen.

### 6.4 Die Zielfunktion – die „Kosten" jeder Variable

Jede Spalte bekommt einen **Kostenwert**; die Zielfunktion ist die Summe
`Kosten × Wert` über alle Spalten, und die soll **minimal** werden.

- **Rezepte:** Kosten je nach Bias – `1` (zähle Maschinen), Strom-MW (zähle Strom)
  oder `0` (bei „Rohstoffe sparen" sind die Maschinen egal).

  ```csharp
  private static Rational RecipeCost(GameDatabase data, RecipeDefinition recipe, PlanBias bias) => bias switch
  {
      PlanBias.Machines => Rational.One,
      PlanBias.Power    => PowerPerMachine(data, recipe),
      _                 => Rational.Zero,
  };
  ```

- **Außenzufuhren (Rohstoffe):** kosten ihre **Seltenheit**. Was du selbst
  bereitstellst, ist gratis („nutze, was ich schon habe").

  ```csharp
  private static Rational SupplyCost(string part, bool provided, PlanBias bias, Dictionary<string, Rational> weights)
  {
      if (provided) return Rational.Zero;       // vorhandener Vorrat ist umsonst
      return bias == PlanBias.Resources ? ScarcityWeights.WeightFor(weights, part) : Rational.Zero;
  }
  ```

#### Woher kommen die Seltenheits-Gewichte?

Aus den **gesamten Fördermengen** der Karte. Je weniger es von einem Rohstoff gibt,
desto „teurer" ist er. Eisenerz ist der Bezugspunkt (Gewicht ≈ 1), Uran ist viel
seltener und damit teuer:

```csharp
// Ficsit.Schematics.Core/Planning/ScarcityWeights.cs
private static readonly Dictionary<string, Rational> ClassicTotals = new()
{
    ["Iron Ore"]   = new Rational(92100),
    ["Copper Ore"] = new Rational(36900),
    ["Crude Oil"]  = new Rational(12600),
    ["Uranium"]    = new Rational(2100),
    // ...
};
// Gewicht = Erz-Gesamtmenge / Stoff-Gesamtmenge:
weights[name] = total.IsPositive ? iron / total : new Rational(1000);
```

Beispiel: Uran-Gewicht = 92100 / 2100 = 43,86… → eine Einheit Uran „kostet" in der
Optimierung also fast 44-mal so viel wie eine Einheit Eisenerz. Deshalb baut der
Planer bei `Resources`-Bias lieber erzlastige Ketten als uranlastige. Wenn du eine
echte Karte importierst, werden die Gewichte aus *deiner* Welt neu berechnet
(`TotalsFromMap`).

> **Warum keine winzigen „Tie-Break"-Kosten?** Im Code steht ein interessanter
> Kommentar: Man könnte gleich teure Lösungen mit Mini-Zuschlägen (Epsilon)
> auseinanderhalten. Das vermeidet der Code bewusst, weil solche krummen Zahlen die
> exakten Brüche „vergiften" (riesige Nenner). Stattdessen sorgt eine
> deterministische Regel im Solver (Bland-Regel, Abschnitt 7) dafür, dass bei
> gleich guten Lösungen **immer dieselbe** herauskommt.

### 6.5 Alles zusammen: die dünne Matrix + der Aufruf

Am Ende werden alle Spalten zu einer **Matrix** zusammengesetzt und an den Solver
übergeben:

```csharp
var baseMatrix = SparseMatrix.FromColumns(rowCount, columns);
solution = RevisedSimplexSolver.Minimize(baseMatrix, b, costArray);
```

`Minimize(A, b, c)` heißt wörtlich: *„Finde die Variablenwerte `x ≥ 0`, sodass
`A·x = b` gilt und die Kosten `c·x` minimal sind."* Das ist die formale Fassung von
allem, was wir bisher besprochen haben:

- `A` = die Tabelle der Rezept-/Zufuhr-Koeffizienten (Spalten × Zeilen),
- `b` = die rechte Seite (Ziele und Obergrenzen),
- `c` = die Kosten pro Variable,
- `x` = die gesuchten Maschinenzahlen und Mengen.

---

## 7. Das Simplex-Verfahren: wie gelöst wird

Jetzt der Rechenkern:
[`RevisedSimplexSolver`](../Ficsit.Schematics.Core/Planning/RevisedSimplexSolver.cs).
Die große Idee kennst du schon aus Abschnitt 5:

> Die optimale Lösung sitzt in einer **Ecke** des zulässigen Bereichs. Es gibt nur
> endlich viele Ecken. Also: von Ecke zu Ecke wandern und dabei immer die Kosten
> senken – bis keine Nachbarecke mehr besser ist. Fertig.

So läuft das Simplex-Verfahren ab. Im Detail:

### 7.1 Was ist eine „Ecke" konkret?

Bei `m` Gleichungen wählt man `m` Variablen aus, die „aktiv" sein dürfen (die
**Basis**), alle anderen setzt man auf `0`. Das ergibt genau einen Punkt – eine
Ecke. „Von Ecke zu Ecke wandern" heißt: **eine Variable rein in die Basis, eine
andere raus.** Dieser Tausch heißt **Pivot**.

```csharp
// RevisedSimplexSolver.cs – Kern der Schleife (vereinfacht beschrieben)
var entering = ...;   // welche Variable soll NEU aktiv werden? (senkt Kosten)
ComputeDirection(entering);
var pivotRow = ...;   // welche Variable muss dafür RAUS? (Verhältnistest)
Pivot(pivotRow, entering);
```

### 7.2 Welche Variable kommt rein? (reduzierte Kosten)

Für jede inaktive Variable berechnet der Solver die **reduzierten Kosten**: „Wenn
ich anfange, diese Variable zu nutzen – wird die Gesamtsumme dann *billiger*?" Eine
**negative** Antwort heißt „ja, lohnt sich":

```csharp
// RevisedSimplexSolver.cs (Iterate)
for (var j = 0; j < totalColumns; j++)
{
    if (_isBasic[j]) continue;
    if (_reducedCosts[j].IsNegative) { entering = j; break; } // erste lohnende nehmen
}
// ...
if (entering < 0) return true; // KEINE Verbesserung mehr möglich → OPTIMAL
```

Wenn **keine** Variable mehr negative reduzierte Kosten hat, kann man nirgends mehr
sparen → **die aktuelle Ecke ist optimal.** Das ist das Abbruchkriterium.

### 7.3 Welche Variable geht raus? (Verhältnistest)

Wenn man die neue Variable hochfährt, sinken irgendwann andere Basis-Variablen auf
`0`. Die **erste**, die `0` erreicht, fliegt raus – sonst würde sie negativ, und
das ist verboten (`x ≥ 0`). Das ist der **Verhältnistest** (ratio test):

```csharp
// RevisedSimplexSolver.cs (Iterate)
for (var i = 0; i < _m; i++)
{
    if (!_direction[i].IsPositive) continue;
    var ratio = BasicValues[i] / _direction[i]; // wie weit kann diese Zeile mit?
    if (pivotRow < 0 || ratio < bestRatio) { pivotRow = i; bestRatio = ratio; }
    // ... Gleichstand: lexikografische Tie-Break-Regel (verhindert Kreiseln)
}
if (pivotRow < 0) return false; // nichts begrenzt das Hochfahren → UNBOUNDED
```

### 7.4 Warum das Verfahren garantiert anhält (Bland-Regel)

Es gibt einen fiesen Sonderfall: **Degeneration**. Dabei kann der Simplex theoretisch
im Kreis laufen – Ecke A → B → C → A → … – und nie fertig werden. Dagegen gibt es
bewährte Regeln. Der Code nutzt zwei davon:

1. **Index-Reihenfolge / Bland-Regel:** Bei der Auswahl „wer kommt rein?" wird
   normalerweise einfach die **erste** lohnende Variable genommen (nicht die
   „beste"). Das klingt naiv, **garantiert aber, dass nie ein Kreis entsteht.**
2. **Lexikografischer Verhältnistest:** Bei Gleichstand im Verhältnistest
   entscheidet eine eindeutige, immer gleiche Reihenfolge
   (`LexicographicallySmaller`). Auch das verhindert Kreiseln beweisbar.

Es gibt sogar einen cleveren Kompromiss: Wenn das Verfahren in vielen
Null-Fortschritt-Schritten „feststeckt", schaltet es **vorübergehend** auf die
schnellere Dantzig-Regel („nimm die *beste*") um, um durchzubrechen, und kehrt
danach zur sicheren Index-Reihenfolge zurück:

```csharp
// RevisedSimplexSolver.cs (Iterate)
if (consecutiveDegenerate < stallThreshold)  { /* Index-Reihenfolge: erste negative */ }
else                                          { /* Dantzig: stärkste negative – „durchbrechen" */ }
```

### 7.5 Phase 1 und Phase 2: erst *irgendeine* Fabrik, dann die *beste*

Ein Henne-Ei-Problem: Simplex wandert von Ecke zu Ecke – aber wo ist die **erste**
Ecke? Bei komplexen Bilanzen ist nicht einmal klar, ob es überhaupt eine gültige
Fabrik gibt. Lösung: das Verfahren läuft in **zwei Phasen**.

- **Phase 1 – „Geht das überhaupt?"** Man fügt **künstliche Hilfsvariablen** hinzu,
  die anfangs die Bilanzen „mit Gewalt" erfüllen, und minimiert dann deren Summe.
  Geht sie auf `0` → es existiert eine echte, gültige Fabrik (ein **zulässiger
  Startpunkt**). Bleibt etwas übrig → **infeasible** (unmöglich, z. B. wenn du den
  einzig nötigen Rohstoff verboten hast).

  ```csharp
  // RevisedSimplexSolver.cs
  if (!state.RunPhase1())
      return new RevisedSolveResult { Status = PlanStatus.Infeasible };
  state.DriveOutArtificials();   // Hilfsvariablen wieder hinausdrängen
  return state.RunPhase2();
  ```

- **Phase 2 – „Und jetzt die beste."** Von diesem gültigen Startpunkt aus wird die
  *echte* Zielfunktion (deine Kosten) minimiert – das eigentliche Ecke-zu-Ecke-
  Wandern aus 7.1–7.3.

Diese drei Ergebnisse können herauskommen:

```csharp
// Ficsit.Schematics.Core/Planning/PlanStatus.cs
public enum PlanStatus
{
    Optimal,    // beste Fabrik gefunden
    Infeasible, // unmöglich (z. B. nötiger Rohstoff verboten)
    Unbounded,  // grenzenlos – nur möglich, wenn beim Maximieren eine Obergrenze fehlt
}
```

### 7.6 „Revised" und `B⁻¹` – das darfst du getrost überfliegen

Das Wort **„revised"** (revidiertes Simplex) und das ständige `BasisInverse` /
`B⁻¹` im Code sind eine **Effizienz-Variante**, kein anderes Verfahren. Kurz, ohne
dass du Matrizenrechnung können musst:

- Die naive Variante führt eine riesige Tabelle (Tableau) mit und rechnet sie bei
  *jedem* Pivot komplett um.
- Die „revised"-Variante merkt sich stattdessen nur ein **kleines Buchhaltungs-
  Hilfsmittel** (`B⁻¹`, ein `m×m`-Feld), mit dem sie die jeweils gebrauchten Zahlen
  *bei Bedarf* schnell ausrechnet. Die große, dünne Rezept-Tabelle `A` bleibt
  unangetastet.

Merksatz: **Gleiches Ergebnis, weniger Rechenarbeit pro Schritt.** Wenn dir die
`B⁻¹`-Zeilen begegnen – das ist genau diese Abkürzung. Für das *Verstehen, was der
Planer tut*, brauchst du sie nicht.

---

## 8. Warum exakte Brüche statt Kommazahlen?

Schau auf das Plastik-Ergebnis: **genau 300** Öl, **genau 0** Abfall. Mit normalen
Computer-Kommazahlen (`double`) wäre das fast unmöglich, denn die rechnen
**gerundet**. `1/3` wird zu `0,33333…` und beim Weiterrechnen sammeln sich
winzige Fehler an. In einem Recyclingkreislauf, wo sich Ströme exakt aufheben
müssen, würde aus „0 Abfall" schnell „0,0000001 Abfall" – und der Planer würde
unnötig eine Senke einbauen oder den Kreislauf gar nicht als geschlossen erkennen.

Deshalb rechnet **alles** mit echten Brüchen:
[`Rational`](../Ficsit.Schematics.Core/Numerics/Rational.cs) – Zähler und Nenner als
ganze Zahlen, immer vollständig gekürzt. `1/3 + 1/3 + 1/3` ergibt **exakt** `1`,
nicht `0,999…`.

Damit das trotzdem schnell ist, gibt es einen klugen Doppelweg
(**„transprecision"**):

```csharp
// Ficsit.Schematics.Core/Numerics/Rational.cs
public static Rational operator +(Rational a, Rational b)
{
    if (!a._isBig && !b._isBig)
    {
        // schneller Weg: passt alles in 64-Bit-Ganzzahlen? Dann ohne Umwege rechnen.
        if (TryMul(a._num, b._den, out var x) && /* ... */ TryMul(a._den, b._den, out var den))
            return SmallFraction(num, den);
        // sonst exakt in 128 Bit ausweichen (kann nicht überlaufen)
        return FromInt128(/* ... */);
    }
    // ganz große Zahlen: BigInteger (beliebig groß, dafür langsamer)
    return new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, /* ... */);
}
```

- **Kleine Zahlen** (passen in 64 Bit): blitzschnelle Ganzzahl-Arithmetik, keine
  Speicheranforderung.
- **Mittlere Zwischenergebnisse:** 128-Bit – kann beweisbar nicht überlaufen.
- **Riesige Zahlen:** automatischer Umstieg auf `BigInteger` (beliebig groß).

Du bekommst also **Exaktheit wie auf dem Papier** und trotzdem fast die
Geschwindigkeit normaler Ganzzahlen. Das Kürzen erledigt der **euklidische
ggT-Algorithmus** in einer hardwarefreundlichen Variante (Steins binärer ggT),
damit Brüche nie unnötig große Zähler/Nenner mitschleppen.

Am Ende, nur für die **Anzeige**, werden Brüche in lesbare Kommazahlen verwandelt
(z. B. „91,6667 %") – aber erst ganz zum Schluss, nachdem exakt gerechnet wurde:

```csharp
// Rational.cs – nur fürs Anzeigen, nicht fürs Rechnen
public string ToDecimalString(int decimalPlaces, RoundingMode mode) { /* ... */ }
```

---

## 9. Warum eine „dünn besetzte" Matrix?

Die Tabelle `A` hat *eine Zeile pro Stoff* und *eine Spalte pro Variable* – schnell
mehrere hundert mal mehrere hundert. Aber: Ein einzelnes Rezept benutzt nur eine
Handvoll Stoffe. In seiner Spalte sind also fast alle Einträge **null**.

> In typischen Fabrik-Tabellen sind **über 98 % der Einträge null.**

Es wäre Verschwendung, all diese Nullen zu speichern und mit ihnen zu multiplizieren
(`irgendwas · 0 = 0`). Deshalb speichert
[`SparseMatrix`](../Ficsit.Schematics.Core/Planning/SparseMatrix.cs) **nur die Werte
≠ 0** – im sogenannten CSC-Format (compressed sparse column):

```csharp
// Ficsit.Schematics.Core/Planning/SparseMatrix.cs
/// Compressed sparse column (CSC): only the non-zero coefficients are stored, so
/// matrix-vector work scales with the entry count instead of the rectangle area.
public Rational[] Values { get; }         // nur die Werte ≠ 0
public int[] RowIndices { get; }          // in welcher Zeile steht jeder Wert
public int[] ColumnPointers { get; }      // wo beginnt jede Spalte in Values
```

Vereinfacht: Statt „1000 Felder, davon 980 leer" steht da nur „20 Werte + die Info,
wo sie hingehören". Der Solver durchläuft beim Rechnen nur diese 20 statt 1000 –
das macht große Pläne überhaupt erst in Sekunden lösbar.

---

## 10. Die zwei Stufen: Maximieren & „mehr habe ich nicht"

Bis hier galt: „Mach mir **genau** 900 Plastik." Es gibt aber zwei Spezialfälle, in
denen der Planer das Problem **in zwei Stufen** löst. Beide drehen sich um eine
Hilfsvariable `t` (die `bundle`-Spalte).

### Fall A: „Maximiere, was aus meinen Rohstoffen rausholbar ist"

Du gibst Rohstoffe mit Obergrenze vor („ich habe 60 Erz/min") und willst das
**Maximum** an Output. Jetzt sind die Ziele keine festen Zahlen mehr, sondern ein
**Verhältnis** (ein „Bündel"), das so groß wie möglich skaliert werden soll. `t` ist
der gemeinsame Skalierungsfaktor.

- **Stufe 1:** Mache `t` so groß wie möglich (*„Wie viel geht überhaupt?"*).

  ```csharp
  // FactoryPlanner.cs – Stufe 1: nur t maximieren
  stage1Costs[bundleColumn] = -Rational.One;   // -t minimieren = t maximieren
  var stage1 = RevisedSimplexSolver.Minimize(baseMatrix, b, stage1Costs);
  achieved = stage1.Values[bundleColumn];
  ```

- **Stufe 2:** Halte `t` auf diesem Maximum **fest** und optimiere *unter allen
  Wegen, die dieses Maximum erreichen*, noch deinen Bias (z. B. wenigste
  Maschinen). Das verhindert „verschwenderische" Lösungen, die zufällig auch das
  Maximum treffen.

### Fall B: „Das ist alles, was ich habe" (exklusive Vorgabe)

Du sagst: „Ich produziere woanders schon 45 Eisenbarren/min, **mehr gibt's nicht**."
Wenn dein Ziel mehr bräuchte, kann es nicht voll erfüllt werden. Dann skaliert der
Planer den **gesamten Output** herunter (`t ≤ 1`):

```csharp
// FactoryPlanner.cs
var scaledTargetMode = !request.MaximizeFromProvisions && exclusiveParts.Count > 0;
// ... bundleEntries[e] = (bundleCapRow, Rational.One); // t ≤ 1
```

Genau das prüft dieser Test – 60 Platten bräuchten 90 Barren, es gibt aber nur 45,
also kommt **die Hälfte** heraus:

```csharp
// FactoryPlannerTests.cs
request.Targets.Add(new PlanTarget("Iron Plate", new Rational(60)));
request.Provisions.Add(new PlanProvision("Iron Ingot", new Rational(45), Exclusive: true));
// ...
Assert.Equal(new Rational(1, 2), plan.AchievedFraction);   // auf 50 % skaliert
Assert.Equal(new Rational(30), plan.Outputs["Iron Plate"]); // → 30 Platten
Assert.Contains("Iron Ingot", plan.Bottlenecks);            // das ist der Engpass
```

Ohne das `Exclusive`-Häkchen würde der Planer die fehlenden Barren stattdessen
**selbst dazubauen** und die vollen 60 Platten liefern.

### Warm-Start: Stufe 2 fängt nicht bei null an

Ein hübsches Detail: Stufe 2 hat fast dieselben Gleichungen wie Stufe 1 (plus eine
Zeile, die `t` festnagelt). Statt komplett neu zu rechnen, übernimmt
`MinimizeWarm` die fertige Ecke aus Stufe 1 und rechnet nur die kleine Änderung
nach:

```csharp
// FactoryPlanner.cs
solution = stage1.Snapshot is { } snapshot
    ? RevisedSimplexSolver.MinimizeWarm(extendedMatrix, bExtended, costArray, snapshot, bundleColumn)
    : RevisedSimplexSolver.Minimize(extendedMatrix, bExtended, costArray);
```

Das ist wie beim Navi: Wenn sich nur das Ziel leicht ändert, rechnet man nicht die
ganze Route neu, sondern passt das Ende an.

---

## 11. Vom Rechenergebnis zur fertigen Fabrik

Der Solver liefert eine Liste von Variablenwerten. `FactoryPlanner.Plan` liest sie
zurück und sortiert sie nach ihrem Buchstaben in ein verständliches `PlanResult`:

```csharp
// FactoryPlanner.cs – Rückübersetzung
switch (kind)
{
    case 'r': result.Recipes.Add(new PlannedRecipe(name, value));  // Rezept + Maschinenzahl
              result.TotalMachines += value;
              result.TotalPowerMW  += value * PowerPerMachine(data, ...); break;
    case 's': result.Supplies[name] = value; break;  // Rohstoff-Zufuhr
    case 'k': result.Sinks[name]    = value; break;  // in die Senke
}
// Welche Vorgaben sind voll ausgereizt? → Flaschenhälse
foreach (var (part, provision) in provisionByPart)
    if (result.Supplies.TryGetValue(part, out var used) && used == provision.Cap)
        result.Bottlenecks.Add(part);
```

Danach baut die UI daraus eine **echte Fabrik auf dem Canvas**
([`BuildPlanOnCanvas`](../MainPage.AutoPlan.cs)):

1. Pro Rezept wird ein Knoten erzeugt, das **Limit = Maschinenzahl** gesetzt.
2. Die Knoten werden in **Abhängigkeitsschichten** angeordnet (Rohstoffe links,
   Endprodukt rechts) – per „längster Pfad", robust gegen Recycling-Schleifen.
3. Jeder Produzent eines Stoffs wird mit jedem Verbraucher verbunden; wird ein
   Stoff in die Senke geschickt, kommt ein AWESOME-Sink-Knoten dazu.

### Krumme Maschinenzahlen → ganze Maschinen (Auto-Round)

Der Planer darf „1,5 Schmelzen" sagen. In der echten Fabrik gibt es aber nur **ganze**
Maschinen. Das löst eine *separate* Funktion (`Auto-Round`, im `BasicSolver` – nicht
im LP-Planer selbst): Sie rundet die Anzahl **auf** und senkt dafür den **Takt**, sodass
der Durchsatz exakt gleich bleibt.

```text
Beispiel aus docs/specs/auto-round.md:
  Lösung verlangt 5,5 Maschinen @ 100 %.
  Auto-Round AN → 6 Maschinen @ 91,6667 %  (6 × 0,91666… = 5,5 → exakt gleicher Output)
```

Auch das rechnet in `Rational`, damit kein Rundungsfehler entsteht. So bleibt die
Zahl im Spiel **direkt einstellbar**: „6 Maschinen, Takt 91,6667 %".

---

## 12. Landkarte: welche Datei macht was

| Datei | Rolle |
|---|---|
| [`Planning/FactoryPlanner.cs`](../Ficsit.Schematics.Core/Planning/FactoryPlanner.cs) | **Herzstück.** Übersetzt die Anfrage in ein LP (Spalten, Zeilen, Kosten) und liest das Ergebnis zurück. |
| [`Planning/RevisedSimplexSolver.cs`](../Ficsit.Schematics.Core/Planning/RevisedSimplexSolver.cs) | Der Löser: Simplex-Verfahren (Phase 1/2, Pivot, Bland-Regel, Warm-Start). |
| [`Numerics/Rational.cs`](../Ficsit.Schematics.Core/Numerics/Rational.cs) | Exakte Bruchrechnung (kein Rundungsfehler), mit schnellem 64-Bit-Weg. |
| [`Planning/SparseMatrix.cs`](../Ficsit.Schematics.Core/Planning/SparseMatrix.cs) | Speichert nur Werte ≠ 0 (Fabrik-Tabellen sind >98 % leer). |
| [`Planning/ScarcityWeights.cs`](../Ficsit.Schematics.Core/Planning/ScarcityWeights.cs) | Wie „teuer" ist jeder Rohstoff (nach Seltenheit / nach Kartendaten). |
| [`Planning/PlanRequest.cs`](../Ficsit.Schematics.Core/Planning/PlanRequest.cs) | Die Eingabe: Ziele, Vorräte, Verbote, Bias, Byproduct-Modus. |
| [`Planning/PlanResult.cs`](../Ficsit.Schematics.Core/Planning/PlanResult.cs) | Die Ausgabe: Rezepte, Maschinen, Zufuhren, Senken, Engpässe. |
| [`Planning/PlanBias.cs`](../Ficsit.Schematics.Core/Planning/PlanBias.cs) · [`ByproductMode.cs`](../Ficsit.Schematics.Core/Planning/ByproductMode.cs) | Die Optimierungs- und Abfall-Optionen. |
| [`MainPage.AutoPlan.cs`](../MainPage.AutoPlan.cs) | UI: Eingabemaske, Aufruf des Planers, Bau der Fabrik aufs Canvas. |
| [`Tests/FactoryPlannerTests.cs`](../Ficsit.Schematics.Tests/FactoryPlannerTests.cs) | Beste Lern-Beispiele: Zero-Waste-Plastik, Bottleneck-Skalierung, Maximieren. |

**Empfohlene Lesereihenfolge im Code:** zuerst `FactoryPlannerTests.cs` (die
Beispiele zeigen, was rein- und rauskommt), dann `FactoryPlanner.Plan` von oben nach
unten, dann bei Interesse der Solver.

---

## 13. Glossar

| Begriff | Bedeutung in einfachen Worten |
|---|---|
| **Variable** | Eine Stellschraube, die wir wählen: Maschinenzahl eines Rezepts oder Menge einer Zufuhr. Im Code: eine **Spalte**. |
| **Nebenbedingung / Constraint** | Eine Regel, die gelten *muss*: Stoffbilanz (rein = raus) oder Obergrenze. Im Code: eine **Zeile**. |
| **Zielfunktion** | Die Formel, die minimal werden soll (Kosten = Rohstoffe / Strom / Maschinen). |
| **Linear** | Verdoppeln der Maschinen verdoppelt den Output – keine `x²`, keine Wechselwirkungen. |
| **Zulässiger Bereich** | Alle Kombinationen, die *alle* Regeln erfüllen – geometrisch ein (hochdimensionales) Vieleck. |
| **Ecke** | Ein „Eckpunkt" dieses Vielecks. Eine optimale Lösung liegt immer in einer Ecke. |
| **Simplex** | Verfahren, das von Ecke zu Ecke wandert und dabei die Kosten senkt, bis es nicht mehr besser geht. |
| **Basis / Pivot** | Die gerade „aktiven" Variablen heißen Basis; ein Tausch (eine rein, eine raus) heißt Pivot = der Schritt zur Nachbarecke. |
| **Reduzierte Kosten** | „Lohnt es sich, diese inaktive Variable zu nutzen?" Negativ = ja. |
| **Slack (Schlupf)** | Hilfsvariable, die aus „≤ 60" eine Gleichung „… + Schlupf = 60" macht. Schlupf 0 = Engpass. |
| **Phase 1 / Phase 2** | Erst *irgendeine* gültige Fabrik finden, dann die *beste*. |
| **Bland-Regel** | Auswahlregel, die garantiert, dass der Simplex nie im Kreis läuft. |
| **Infeasible / Unbounded** | Unmöglich (z. B. nötiger Rohstoff verboten) / grenzenlos (fehlende Obergrenze beim Maximieren). |
| **Rational** | Exakte Bruchzahl (Zähler/Nenner) statt fehleranfälliger Kommazahl. |
| **Sparse / CSC** | Nur die Werte ≠ 0 speichern; spart Speicher und Rechenzeit. |
| **Bias** | Wonach optimiert wird: Rohstoffe, Strom oder Maschinen. |
| **Bundle / `t`** | Skalierungsfaktor beim Maximieren bzw. beim Herunterskalieren des Ziels. |

---

### In drei Sätzen

Der Planer schreibt deine Anfrage als großes Gleichungssystem mit einem
Sparziel auf (lineares Programm): eine Variable pro Rezept und Zufuhr, eine
„rein = raus"-Gleichung pro Stoff, ein Preis pro Variable. Das Simplex-Verfahren
wandert dann von Ecke zu Ecke des Lösungsraums und senkt die Kosten, bis die beste
Fabrik gefunden ist. Weil alles in exakten Brüchen und einer platzsparenden
dünnen Matrix gerechnet wird, kommen saubere Ergebnisse wie „genau 300 Öl, null
Abfall" heraus – und Recyclingkreisläufe ergeben sich von selbst.
```