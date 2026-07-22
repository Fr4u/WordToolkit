param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeExecutable
)

$ErrorActionPreference = "Stop"
$totalWatch = [Diagnostics.Stopwatch]::StartNew()
$runtime = [IO.Path]::GetFullPath($RuntimeExecutable)
if (-not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
    throw "Native executable not found: $runtime"
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sampleImage = Join-Path $repositoryRoot "examples\generated\word-capability-map.png"
if (-not (Test-Path -LiteralPath $sampleImage -PathType Leaf)) {
    throw "Atlas image not found: $sampleImage"
}

$desktop = [Environment]::GetFolderPath("Desktop")
$stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$suffix = [Guid]::NewGuid().ToString("N").Substring(0, 6)
$documentPath = Join-Path $desktop "WordToolkit-Atlas-Worda-$stamp-$suffix.docx"
$pdfPath = Join-Path $desktop "WordToolkit-Atlas-Worda-$stamp-$suffix.pdf"
$commentMarker = "WT_ATLAS_COMMENT_$suffix"
$replaceMarker = "WT_ATLAS_DRAFT_$suffix"
$replacementMarker = "WT_ATLAS_VERIFIED_$suffix"
$bookmarkName = "WTAtlasPoint_$suffix"

$startInfo = [Diagnostics.ProcessStartInfo]::new($runtime)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
[void]$process.Start()

$requestId = 0
$toolCalls = 0
$stageTimings = [Collections.Generic.List[object]]::new()

function Invoke-Mcp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [hashtable]$Params
    )

    $script:requestId++
    $request = @{
        jsonrpc = "2.0"
        id = $script:requestId
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 80 -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()
    $line = $process.StandardOutput.ReadLine()
    if (-not $line) {
        throw "Native MCP exited: $($process.StandardError.ReadToEnd())"
    }
    $response = $line | ConvertFrom-Json -Depth 80
    if ($response.error) {
        throw ($response.error | ConvertTo-Json -Depth 40 -Compress)
    }
    return $response
}

function Invoke-Tool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [hashtable]$Arguments
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-Mcp `
            -Method "tools/call" `
            -Params @{
                name = "execute_wordtoolkit_action"
                arguments = @{
                    action = $Name
                    arguments = $Arguments
                    response_mode = "full"
                }
            }
        if ($response.result.isError) {
            throw (
                $response.result.structuredContent.error |
                    ConvertTo-Json -Depth 40 -Compress
            )
        }
        $watch.Stop()
        $script:toolCalls++
        $stageTimings.Add(
            [pscustomobject]@{
                tool = $Name
                milliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
                status = "passed"
            }
        )
        return $response.result.structuredContent.data
    }
    catch {
        $watch.Stop()
        $stageTimings.Add(
            [pscustomobject]@{
                tool = $Name
                milliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
                status = "failed"
                error = $_.Exception.Message
            }
        )
        throw
    }
}

function Invoke-ExpectedToolError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [hashtable]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedCode
    )

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = Invoke-Mcp `
        -Method "tools/call" `
        -Params @{
            name = "execute_wordtoolkit_action"
            arguments = @{
                action = $Name
                arguments = $Arguments
                response_mode = "full"
            }
        }
    $watch.Stop()
    if (-not $response.result.isError) {
        throw "Expected $Name to reject the operation"
    }
    $errorData = $response.result.structuredContent.error
    if ($errorData.code -ne $ExpectedCode) {
        throw "Expected $ExpectedCode from $Name, got $($errorData.code)"
    }
    $script:toolCalls++
    $stageTimings.Add(
        [pscustomobject]@{
            tool = $Name
            milliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
            status = "guard_passed"
        }
    )
    return $errorData
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

function HeadingOperation {
    param(
        [string]$Text,
        [bool]$PageBreak = $false,
        [double]$Size = 20
    )
    return @{
        type = "text"
        text = $Text
        as_new_paragraph = $true
        formatting = @{
            font_name = "Aptos Display"
            font_size_pt = $Size
            font_color_rgb = "#17365D"
            bold = $true
            paragraph_alignment = "left"
            space_before_pt = 8
            space_after_pt = 10
            keep_with_next = $true
            page_break_before = $PageBreak
        }
    }
}

function BodyOperation {
    param(
        [string]$Text,
        [bool]$Lead = $false
    )
    return @{
        type = "text"
        text = $Text
        as_new_paragraph = $true
        formatting = @{
            font_name = "Aptos"
            font_size_pt = 10.5
            font_color_rgb = if ($Lead) { "#2457A6" } else { "#222222" }
            bold = $Lead
            paragraph_alignment = "justify"
            first_line_indent_pt = if ($Lead) { 0 } else { 18 }
            space_after_pt = 7
            widow_control = $true
        }
    }
}

$chapters = @(
    @{
        title = "1. Dokument jako żywa struktura"
        lead = "Word nie jest pustą kartką z tekstem. Jest warstwowym dokumentem, w którym treść, style, sekcje, relacje, pola, obiekty i historia zmian istnieją równolegle."
        paragraphs = @(
            "Najprostszy akapit ma tekst, zakres znaków, znak końca akapitu i formatowanie akapitowe. Nad nim działają style, motyw, ustawienia językowe i reguły układu strony. Obok niego mogą istnieć komentarze, rewizje, zakładki, pola, przypisy, równania i obiekty graficzne. Pełna automatyzacja musi rozumieć tę warstwowość, ponieważ zmiana pozornie niewielka może przesunąć zakresy, unieważnić token wyboru albo zmienić paginację całego rozdziału.",
            "WordToolkit pracuje na prawdziwym dokumencie otwartym w Microsoft Word. Nie rekonstruuje pliku z uproszczonego modelu i nie udaje edycji przez zapisanie drugiej kopii obok. Każda mutacja ma wersję, ograniczony cel i natywną weryfikację. To nie jest gwarancja nieomylności Worda; to mechanizm, który pozwala wykryć rozjazd, wycofać transakcję i nie zostawić dokumentu w połowie zmiany.",
            "Ten atlas rozdziela trzy rzeczy, które zwykle miesza marketing: to, co wtyczka potrafi edytować dedykowanym narzędziem; to, co potrafi bezpiecznie odczytać lub wywołać przez katalog modelu obiektowego; oraz to, co rozpoznaje, ale blokuje z powodu skutków zewnętrznych, globalnych albo niezweryfikowanych. $replaceMarker"
        )
    },
    @{
        title = "2. Wprowadzanie i redagowanie tekstu"
        lead = "Pisanie w Wordzie obejmuje więcej niż dopisywanie znaków: liczy się miejsce wstawienia, zastępowanie zaznaczenia, zachowanie stylu, historia cofania i reakcja funkcji recenzji."
        paragraphs = @(
            "Wtyczka może dopisać tekst na końcu dokumentu, przy aktywnym kursorze albo w dokładnie zweryfikowanym zaznaczeniu. Token zaznaczenia wiąże pozycję z dokumentem, wersją, oknem, historią i otaczającym tekstem. Jeśli użytkownik przesunie kursor albo inny proces zmieni zawartość, stary token przestaje być ważny. Ta pozorna surowość chroni przed wklejeniem poprawnej treści w złe miejsce.",
            "Długie generowanie odbywa się partiami. Model przygotowuje spójne akapity, a runtime wysyła do Worda jeden większy ładunek, śledzi zakresy i dopiero potem nakłada formatowanie. Koszt nie rośnie wtedy z każdym słowem. Pisanie po tokenie wygląda efektownie przez kilka sekund, ale przy długim dokumencie zamienia się w wolną procesję setek wywołań COM i mnoży miejsca, w których może dojść do rozjazdu.",
            "Natywne wyszukiwanie i zastępowanie korzysta z mechanizmu Range.Find. Obsługuje dopasowanie wielkości liter, całe słowa, symbole specjalne Worda oraz ograniczoną liczbę wyników. Zastępowanie najpierw odkrywa pełny zestaw trafień, sprawdza limit, a potem wykonuje jedną transakcję Undo. Dzięki temu nie zostaje połowa starej i połowa nowej terminologii."
        )
    },
    @{
        title = "3. Typografia i formatowanie znaków"
        lead = "Typografia jest systemem decyzji: krój, stopień, kolor, nacisk i hierarchia muszą działać razem, a nie walczyć o uwagę."
        paragraphs = @(
            "Formatowanie znaków obejmuje rodzinę fontu, rozmiar, kolor, pogrubienie, kursywę, podkreślenie, przekreślenie, kapitaliki, wersaliki i tekst ukryty. Word potrafi łączyć formatowanie bezpośrednie ze stylem znaku i stylem akapitu. Automatyzacja musi wiedzieć, czy zmienia lokalny wyjątek, czy element systemu. W tym atlasie tytuły, leady i tekst główny mają odmienne, konsekwentne parametry.",
            "Nadmierna liczba fontów nie jest bogactwem, tylko hałasem. Dokument techniczny zwykle potrzebuje jednej rodziny do tekstu, jednej do nagłówków i ewentualnie fontu monospaced dla kodu. Kolor powinien przekazywać funkcję: granat buduje hierarchię, szarość usuwa ciężar z metadanych, a czerwony powinien zostać dla ostrzeżeń. Losowe barwy i ręczne pogrubienia rozrywają strukturę.",
            "Word przechowuje część formatowania na poziomie zakresu. Jeśli zaznaczenie obejmuje fragmenty o różnych ustawieniach, właściwość może zwrócić stan mieszany. Dlatego WordToolkit wymaga świeżego, niepustego zaznaczenia i stosuje tylko jawnie podane parametry. Nie nadpisuje reszty stylu przez przypadek."
        )
    },
    @{
        title = "4. Akapity, wcięcia i rytm strony"
        lead = "Czytelność długiego tekstu rodzi się w akapicie: w odstępach, wcięciach, wyrównaniu i kontroli podziału stron."
        paragraphs = @(
            "Word rozróżnia odstęp przed akapitem, odstęp po nim, wcięcie lewe i prawe oraz wcięcie pierwszego wiersza. Te wartości powinny zastępować puste akapity i ręczne spacje. Justowanie ma sens w dłuższych blokach, lecz krótkie noty lepiej wyrównać do lewej. Wcięcie pierwszego wiersza porządkuje narrację, ale nie powinno pojawiać się bezpośrednio pod nagłówkiem.",
            "Reguły KeepWithNext, KeepTogether, WidowControl i PageBreakBefore decydują o tym, czy nagłówek zostanie z następnym akapitem, czy blok nie rozpadnie się przypadkowo i czy na stronie nie zostanie samotny wiersz. Ten dokument wymusza nową stronę dla każdego rozdziału i utrzymuje nagłówki z tekstem. To nie jest ozdoba: to kontrola przepływu informacji.",
            "Diagnoza układu może policzyć akapity i wskazać podejrzane kombinacje bez zwracania całej treści. Nie zastępuje oczu człowieka. Word zależy od fontów, sterownika drukarki, ustawień zgodności i późniejszych zmian. Automatyczna kontrola ma wykryć sygnały ryzyka, nie ogłosić estetyczną doskonałość."
        )
    },
    @{
        title = "5. Style, motywy i szablony"
        lead = "Styl jest kontraktem semantycznym. Mówi, czym jest fragment, a dopiero potem jak ma wyglądać."
        paragraphs = @(
            "Nagłówki, tytuły, cytaty, podpisy i tekst podstawowy powinny korzystać z konsekwentnej hierarchii. Style umożliwiają globalną zmianę wyglądu, nawigację, automatyczny spis treści i stabilny eksport. Formatowanie bezpośrednie jest dobre dla kontrolowanego wyjątku, ale dokument zbudowany wyłącznie z wyjątków nie ma kręgosłupa.",
            "Motyw Worda wiąże kolory, fonty i efekty. Szablon może dostarczyć gotowe style, marginesy, nagłówki, numerację, pola i elementy marki. Wtyczka zachowuje istniejące style i potrafi przypisywać nazwany styl, ale nie powinna zgadywać lokalnej nazwy stylu ani przebudowywać firmowego szablonu bez jawnego wzorca.",
            "Pełna obsługa galerii stylów, organizatora szablonów, zestawów motywów i wszystkich dziedziczonych właściwości wymaga osobnych, zweryfikowanych edytorów. Katalog COM potrafi wykryć te obiekty, lecz samo istnienie metody nie dowodzi bezpiecznego skutku. Nieznana mutacja pozostaje zablokowana."
        )
    },
    @{
        title = "6. Układ strony, sekcje i kolumny"
        lead = "Strona w Wordzie nie jest stałym płótnem. Powstaje z ustawień sekcji, rozmiaru papieru, marginesów, orientacji, kolumn i treści."
        paragraphs = @(
            "Sekcje pozwalają zmieniać orientację, marginesy, numerację stron, kolumny, nagłówki i stopki wewnątrz jednego dokumentu. Przerwa sekcji nie jest zwykłym znakiem nowej strony. Niewłaściwe jej usunięcie może przenieść ustawienia z następnej części i rozsypać cały układ. Dlatego edycja sekcji wymaga bardziej świadomego narzędzia niż dopisanie tekstu.",
            "Word obsługuje rozmiary papieru, marginesy lustrzane, miejsce na oprawę, pionowe wyrównanie, kierunek tekstu i siatki dokumentu. Część tych funkcji jest regionalna lub zależy od drukarki. WordToolkit obecnie diagnozuje sekcje i potrafi ustawić zawartość nagłówków i stopek, ale nie wystawia surowego edytora wszystkich właściwości PageSetup.",
            "W atlasie każda część rozpoczyna się od nowej strony za pomocą właściwości akapitu, nie przez serię pustych wierszy. To zachowuje strukturę podczas dopisywania wcześniejszych rozdziałów. Pagina może się przesunąć, ale intencja pozostaje."
        )
    },
    @{
        title = "7. Nagłówki, stopki i numeracja"
        lead = "Nagłówek i stopka są odrębnymi historiami dokumentu, powiązanymi z sekcją i wariantem strony."
        paragraphs = @(
            "Word rozróżnia wariant podstawowy, pierwszą stronę oraz strony parzyste. Każdy wariant może dziedziczyć treść z poprzedniej sekcji albo zostać odłączony. Automatyzacja musi jawnie ustawić LinkToPrevious, inaczej zmiana lokalna może rozlać się po rozdziałach lub przeciwnie — zniknąć tam, gdzie oczekiwano dziedziczenia.",
            "Stopka często łączy numer bieżącej strony z liczbą wszystkich stron, nazwą dokumentu, datą albo klasyfikacją. Pola Worda są dynamiczne i ich wynik zależy od aktualizacji oraz paginacji. WordToolkit wstawia tylko typowane, dozwolone pola; nie przyjmuje surowego kodu pola, który mógłby sięgać do plików, danych zewnętrznych lub automatyzacji.",
            "Ten atlas tworzy warianty nagłówka i stopki natywnie. Zawartość pozostaje edytowalna w Wordzie. Zaawansowane rysunki, pola i połączone obiekty w nagłówkach wymagają dalszych dedykowanych operacji."
        )
    },
    @{
        title = "8. Listy, konspekty i numeracja wielopoziomowa"
        lead = "Lista Worda jest obiektem numeracji, a nie zbiorem ręcznie wpisanych myślników i cyfr."
        paragraphs = @(
            "Lista punktowana i numerowana korzysta z ListFormat oraz definicji szablonu listy. Dzięki temu elementy można przesuwać, kontynuować i ponownie numerować. Ręcznie wpisane prefiksy wyglądają podobnie tylko do pierwszej edycji; po dodaniu punktu numeracja zaczyna kłamać.",
            "Konspekt wielopoziomowy łączy poziomy listy ze stylami nagłówków. To podstawa dokumentów prawnych, specyfikacji i instrukcji. WordToolkit ma szybkie narzędzia do płaskich list punktowanych i numerowanych. Wielopoziomowe schematy, restartowanie w zagnieżdżeniach i firmowe wzorce numeracji pozostają obszarem wymagającym osobnego edytora.",
            "W dalszej części dokumentu znajdują się prawdziwe listy Worda utworzone jednym ładunkiem, bez operacji element po elemencie. Są to obiekty edytowalne, a nie tekst udający strukturę."
        )
    },
    @{
        title = "9. Tabele i obliczenia"
        lead = "Tabela łączy układ, dane i pola obliczeniowe. Jej siatka może być prosta albo pełna scalonych komórek, zagnieżdżeń i wyjątków."
        paragraphs = @(
            "WordToolkit tworzy prostokątną tabelę z jednego ładunku tekstowego, a następnie konwertuje zakres przez natywne Range.ConvertToTable. Ogranicza to ruch COM i ryzyko pozostawienia połowy wiersza. Można wskazać styl, dopasowanie do zawartości, okna lub szerokości stałej oraz wyróżnienie pierwszego wiersza.",
            "Pola formuł w tabelach potrafią sumować, liczyć średnią, liczbę elementów, minimum, maksimum i iloczyn. Operacja przyjmuje współrzędne oraz typowane źródło ABOVE, BELOW, LEFT, RIGHT albo jawny prostokątny zakres komórek. Surowy kod pola jest odrzucony. W dalszej części atlasu tabela ma formuły obliczane przez Worda.",
            "Scalone komórki, nieregularna siatka, sortowanie, powtarzane nagłówki, podział wierszy i zaawansowane obramowania należą do większej powierzchni tabel. Katalog widzi odpowiednie obiekty, ale nie wolno nazywać ich pełną obsługą bez transakcji i weryfikacji skutku."
        )
    },
    @{
        title = "10. Obrazy, kształty, wykresy i multimedia"
        lead = "Warstwa graficzna Worda obejmuje obrazy w tekście, obiekty pływające, kształty, pola tekstowe, SmartArt, wykresy i osadzone pliki."
        paragraphs = @(
            "Obraz w tekście zachowuje się jak znak i płynie z akapitem. Obiekt pływający ma zakotwiczenie, zawijanie, pozycję względem strony i warstwy. WordToolkit bezpiecznie osadza obrazy jako InlineShape, ustawia rozmiar, blokadę proporcji, tytuł i tekst alternatywny. Źródło musi być jawnym lokalnym plikiem w dozwolonym formacie.",
            "Kształty, pola tekstowe i diagramy SmartArt mają rozbudowane kolekcje, układ, grupowanie, łącza i efekty. Wykresy korzystają z osadzonych danych i osobnego modelu obiektowego. OLE może uruchamiać zewnętrzne aplikacje. Te obszary nie powinny być otwarte przez surowy wykonawca nazw; wymagają ograniczonych kontraktów, kontroli ścieżek i sprawdzania osadzonej zawartości.",
            "Atlas zawiera natywnie osadzony obraz z tekstem alternatywnym. To dowód obsługi jednego bezpiecznego toru graficznego, nie deklaracja pełnej kontroli nad każdym obiektem DrawingML."
        )
    },
    @{
        title = "11. Zakładki, pola i odsyłacze"
        lead = "Zakładka nadaje nazwę zakresowi. Pole wstawia wynik obliczany przez Worda. Razem budują dokument, który potrafi odwoływać się do własnej struktury."
        paragraphs = @(
            "Zakładki mogą oznaczać tytuł, definicję, wynik albo miejsce docelowe. Ich nazwy mają ograniczenia, a zakres przesuwa się wraz z edycją tekstu. WordToolkit tworzy nowe, niekolidujące zakładki nad świeżo wstawioną treścią. Nie zastępuje po cichu istniejącej nazwy i nie przyjmuje dowolnego zakresu pochodzącego ze starej wersji dokumentu.",
            "Bezpieczne pola obejmują między innymi numer strony, liczbę stron, datę, czas, nazwę pliku, liczbę słów i znaków, sekwencje, odwołania do zakładek oraz ograniczone formuły. Każdy rodzaj ma osobne argumenty. Pole jest tworzone jako natywny Field i aktualizowane przez Worda.",
            "Spis treści, indeks, bibliografia, cytowania i podpisy korzystają z szerszej rodziny pól i kolekcji. Część można rozpoznać w dokumencie, lecz bieżący natywny edytor nie wystawia arbitralnych kodów. To celowa bariera: pola mogą odwoływać się do plików, baz danych i automatyzacji."
        )
    },
    @{
        title = "12. Przypisy, podpisy i aparat naukowy"
        lead = "Długi tekst bez aparatu odsyłaczy zamienia źródła i wyjaśnienia w ścianę nawiasów."
        paragraphs = @(
            "Przypis dolny istnieje w osobnej historii dokumentu i ma automatyczny znacznik w tekście głównym. Przypis końcowy trafia do końca dokumentu lub sekcji. WordToolkit może utworzyć oba rodzaje z natywnych kolekcji Worda oraz sprawdzić, czy licznik wzrósł. W tym pliku występują oba.",
            "Podpisy pod tabelami i ilustracjami zwykle opierają się na polu SEQ. Odsyłacze używają REF do zakładki. Taki układ pozwala aktualizować numerację po przesunięciu elementu. Atlas pokazuje sekwencję i odwołanie, ale nie udaje kompletnego menedżera bibliografii ani automatycznego indeksu rzeczowego.",
            "Cytowania Worda, źródła bibliograficzne, style APA i ISO oraz pliki źródeł mają własny model i zachowania wersji. Bezpieczna automatyzacja powinna przyjmować strukturalne dane źródła, a nie wstrzykiwać niesprawdzony XML."
        )
    },
    @{
        title = "13. Równania i symbole"
        lead = "Równanie w Wordzie powinno pozostać natywnym, edytowalnym obiektem Office Math, a nie zrzutem ekranu."
        paragraphs = @(
            "WordToolkit przyjmuje LaTeX, UnicodeMath, Presentation MathML i ograniczony OMML. Markup XML jest parsowany bez DTD i zewnętrznych encji, sprawdzany pod kątem przestrzeni nazw, głębokości i liczby elementów, a następnie konwertowany do liniowej notacji Worda. Word tworzy OMath i buduje profesjonalny układ.",
            "Ułamki, pierwiastki, indeksy, sumy, całki, macierze i układy przypadków są elementami strukturalnymi. Natywny runtime sprawdza utworzenie i końcową liczbę równań. Nie porównuje jeszcze pełnego AST z OMML wygenerowanym przez Worda, dlatego skomplikowana notacja nadal wymaga inspekcji człowieka.",
            "W dalszej części dokumentu znajdują się równania pochodzące ze wszystkich czterech wejść. Można kliknąć je w Wordzie, wejść do edytora równań i zmienić składniki. To odróżnia prawdziwą automatykę matematyczną od wklejonego obrazka."
        )
    },
    @{
        title = "14. Wyszukiwanie, nawigacja i struktury"
        lead = "Nawigacja po dużym dokumencie wymaga nagłówków, zakładek, wyszukiwania oraz mapy obiektów, a nie zwracania całej treści do modelu."
        paragraphs = @(
            "Word ma historie tekstu głównego, nagłówków, stopek, przypisów, komentarzy i pól tekstowych. Zakres w jednej historii nie jest wymienny z zakresem w innej. Mapa struktur WordToolkit liczy siedemnaście rodzajów historii i dwadzieścia trzy kolekcje, nie kopiując ich pełnej treści.",
            "Inspekcja jednej kolekcji jest stronicowana i ograniczona. Może zwrócić typ, pozycję, identyfikatory oraz krótki podgląd tylko wtedy, gdy został jawnie zażądany. To pozwala wykryć tabele, komentarze, pola, zakładki, kształty i kontrolki bez zalewania kontekstu dokumentem.",
            "Katalog modelu obiektowego skanuje bibliotekę typów zainstalowanej wersji Worda. Na tej maszynie znalazł setki typów i ponad dwanaście tysięcy elementów. Katalog jest mapą, nie pozwoleniem. Każda wykonywalna zdolność ma stabilny identyfikator i klasyfikację bezpieczeństwa."
        )
    },
    @{
        title = "15. Komentarze, śledzenie zmian i recenzja"
        lead = "Recenzja musi odróżniać sugestię, komentarz, rewizję i ostateczną treść. Bez tego historia dokumentu staje się śmietnikiem."
        paragraphs = @(
            "Komentarz jest przypięty do zakresu i ma autora, datę, treść, odpowiedzi oraz stan rozwiązania zależny od wersji Worda. WordToolkit tworzy komentarz na świeżym tokenie zakresu lub zaznaczenia. Inspekcja wydaje token recenzji związany z aktualnym elementem, dzięki czemu odpowiedź, usunięcie lub rozwiązanie nie trafia do innego komentarza po zmianie dokumentu.",
            "Track Changes zapisuje wstawienia, usunięcia i zmiany formatowania jako rewizje. Akceptacja i odrzucenie są destrukcyjne dla historii, dlatego wymagają aktualnego tokenu. W atlasie śledzenie zmian zostanie włączone, powstanie jeden jawny wpis kontrolny, a potem tryb zostanie wyłączony bez automatycznego zaakceptowania rewizji.",
            "Porównywanie dokumentów, współtworzenie w chmurze, obecność innych autorów, blokady i komentarze nowego modelu są szersze niż klasyczny COM. Wtyczka potrafi pracować z lokalnym obiektem Worda, lecz nie zastępuje protokołów OneDrive i SharePoint. $commentMarker"
        )
    },
    @{
        title = "16. Formularze, kontrolki zawartości i korespondencja seryjna"
        lead = "Word potrafi być formularzem i generatorem korespondencji, ale te funkcje dotykają danych zewnętrznych, więc wymagają ostrzejszych granic."
        paragraphs = @(
            "Kontrolki zawartości mogą mieć tytuł, tag, placeholder, blokadę, listę wyboru, mapowanie XML i powtarzane sekcje. Starsze pola formularzy mają osobny model. WordToolkit potrafi je wykryć i opisać w mapie struktur, ale nie otwiera ogólnej mutacji bez dedykowanego kontraktu. Błędna zmiana blokady lub mapowania może zniszczyć szablon biznesowy.",
            "Korespondencja seryjna łączy dokument główny ze źródłem danych, filtrami, rekordami i miejscem docelowym. Może otwierać pliki, bazy, pocztę i drukowanie. To obszar skutków zewnętrznych. Surowe wywołania MailMerge, OpenDataSource i Execute pozostają zablokowane.",
            "Bezpieczna przyszła obsługa powinna rozdzielić import danych, podgląd rekordów, generowanie lokalnego dokumentu wynikowego i wysyłkę. Jedno polecenie robiące wszystko byłoby wygodne tylko do chwili, gdy wyśle błędny dokument do tysiąca osób."
        )
    },
    @{
        title = "17. Dostępność, język i korekta"
        lead = "Dokument nie jest poprawny, jeśli człowiek korzystający z czytnika ekranu nie potrafi zrozumieć jego struktury."
        paragraphs = @(
            "Dostępność wymaga hierarchii nagłówków, kolejności czytania, tekstów alternatywnych, sensownych nazw łączy, nagłówków tabel i unikania informacji przekazywanych wyłącznie kolorem. Obraz w atlasie otrzymuje tekst alternatywny. Tabele mają jawny wiersz nagłówkowy, a rozdziały konsekwentną hierarchię wizualną.",
            "Word obsługuje język fragmentu, sprawdzanie pisowni i gramatyki, dzielenie wyrazów, słowniki niestandardowe, tłumaczenie i statystyki czytelności. Część tych funkcji zależy od zainstalowanych pakietów językowych i usług. Obecny natywny zestaw nie deklaruje pełnej automatyzacji korekty ani akceptacji sugestii edytora.",
            "Automatyczny audyt może policzyć brakujące teksty alternatywne lub niewłaściwą strukturę, ale nie oceni, czy opis obrazu naprawdę przekazuje jego znaczenie. Dostępność jest relacją z odbiorcą, nie tylko zestawem pól do odhaczenia."
        )
    },
    @{
        title = "18. Ochrona, prywatność i granice automatyzacji"
        lead = "Najbardziej niebezpieczne funkcje Worda są niebezpieczne właśnie dlatego, że wykraczają poza dokument."
        paragraphs = @(
            "Makra, DDE, zdarzenia, dodatki COM, automatyzacja OLE, łącza zewnętrzne, poczta, drukowanie i publikowanie do sieci mogą uruchamiać kod albo wpływać na inne systemy. WordToolkit blokuje te kategorie w ogólnym wykonawcy. Otwieranie dokumentów wymusza wyłączenie makr i aktualizacji łączy, a ścieżki muszą być bezwzględne.",
            "Hasła, ochrona dokumentu, podpisy cyfrowe, Information Rights Management i etykiety poufności mają skutki, których nie można bezpiecznie cofnąć prostym Undo. Rozpoznanie metod w bibliotece typów nie daje prawa do ich wywołania. Każda taka funkcja potrzebuje jawnego celu, potwierdzenia i sprawdzalnego wyniku.",
            "Dane uczące w bieżącym procesie są agregatami: liczba sukcesów, porażek i zaobserwowanych typów. Formuły, treść dokumentu, nazwy i ścieżki nie trafiają do tych liczników. Po zamknięciu procesu katalog i statystyki znikają."
        )
    },
    @{
        title = "19. Zapis, formaty, PDF i walidacja"
        lead = "Zapis jest momentem, w którym żywy stan Worda staje się trwałym pakietem plików i relacji."
        paragraphs = @(
            "DOCX jest archiwum OPC zawierającym XML, relacje, media, style, numerację, ustawienia i właściwości. Walidacja Open XML SDK może wykryć błędny schemat, relacje i część uszkodzeń strukturalnych. Nie potwierdza jednak sensu treści, wyglądu ani poprawności matematycznej równania.",
            "WordToolkit zapisuje istniejący dokument przez Document.Save, bez ukrytego Save As. Walidacja wymaga stanu zapisanego i pracuje na tymczasowym snapshotcie. Eksport PDF korzysta z natywnego renderera Worda, najpierw tworzy niepusty plik tymczasowy, a dopiero potem przenosi go do docelowej ścieżki.",
            "Word otwiera również DOC, DOCM, ODT, RTF, TXT, HTML, XML i PDF, lecz konwersja może tracić funkcje. Makrozdolny format nie oznacza zgody na uruchomienie makra. Drukowanie pozostaje skutkiem zewnętrznym i nie jest ukryte pod ogólną komendą wykonania."
        )
    },
    @{
        title = "20. Uczciwa definicja pełnego pokrycia"
        lead = "Pełne pokrycie nie oznacza, że model może wywołać każdą metodę. Oznacza, że powierzchnia jest znana, sklasyfikowana i ma bezpieczne gardło wykonawcze."
        paragraphs = @(
            "Dedykowane narzędzia obsługują najczęstsze i najlepiej weryfikowalne operacje: tekst, formatowanie, Find i Replace, tabele, formuły, listy, zakładki, pola, obrazy, komentarze, rewizje, przypisy, nagłówki, równania, zapis, PDF i walidację. Każde ma limity, wersjonowanie i kontrolę skutku.",
            "Katalog COM opisuje publiczne typy i elementy dostępne w lokalnej wersji Worda. Wykonawca przyjmuje stabilne identyfikatory zdolności, typowane cele i argumenty. Operacje zewnętrzne, globalne, zdarzeniowe, ograniczone, wrażliwe i nieznane mutacje są blokowane. To jest szersza wiedza niż lista ręcznych narzędzi, ale węższa władza niż surowy COM.",
            "Dlatego poprawne zdanie brzmi: WordToolkit zna publiczny model obiektowy zainstalowanego Worda, ma zweryfikowane narzędzia dla szerokiego rdzenia pracy z dokumentem i świadomie nie udaje pełnej kontroli nad nieudokumentowanym interfejsem, usługami chmurowymi ani skutkami zewnętrznymi. Wszystko inne byłoby reklamą oderwaną od kodu."
        )
    }
)

$documentId = ""
$version = 0L
$documentOpen = $false
$stage = "initialize"
$failure = $null
$report = [ordered]@{
    runtime = $runtime
    transport = "real MCP STDIO"
    python_used = $false
    document_path = $documentPath
    pdf_path = $pdfPath
    chapter_count = $chapters.Count
}

try {
    [void](Invoke-Mcp `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-06-18"
            capabilities = @{}
            clientInfo = @{
                name = "wordtoolkit-word-capability-atlas"
                version = "1"
            }
        })

    $stage = "verify token-lean public tool surface"
    $catalog = Invoke-Mcp -Method "tools/list" -Params @{}
    Assert-True `
        -Condition ($catalog.result.tools.Count -eq 14) `
        -Message "Expected 14 exposed tools"

    $stage = "list documents"
    $listed = Invoke-Tool -Name "list_live_word_documents" -Arguments @{}

    $stage = "start or attach Word"
    $started = Invoke-Tool `
        -Name "start_word_application" `
        -Arguments @{ visible = $true }
    Assert-True `
        -Condition ($started.word_running -and $started.visible) `
        -Message "Word is not visible"

    $stage = "verify quit guard"
    [void](Invoke-ExpectedToolError `
        -Name "quit_word_application" `
        -Arguments @{
            save_changes = "discard_all"
            confirm = $false
        } `
        -ExpectedCode "AUTH_FORBIDDEN")

    $stage = "create atlas"
    $created = Invoke-Tool `
        -Name "create_live_word_document" `
        -Arguments @{
            output_path = $documentPath
            activate = $true
        }
    $documentId = $created.live_document_id
    $version = [long]$created.live_version
    $documentOpen = $true

    $stage = "inspect empty atlas"
    $emptyInspection = Invoke-Tool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition ($emptyInspection.document.full_name -eq $documentPath) `
        -Message "Atlas handle points to the wrong document"

    $stage = "create title pages"
    $frontOperations = @(
        @{
            type = "text"
            text = "ATLAS MICROSOFT WORD"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 32
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "center"
                space_before_pt = 80
                space_after_pt = 12
                keep_with_next = $true
            }
        },
        @{
            type = "text"
            text = "Pełny demonstrator WordToolkit 0.18"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#2457A6"
                paragraph_alignment = "center"
                space_after_pt = 24
            }
        },
        @{
            type = "text"
            text = "Tekst, typografia, układ, listy, tabele, formuły, pola, zakładki, grafika, przypisy, recenzja, równania, automatyzacja, bezpieczeństwo i eksport."
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 13
                italic = $true
                paragraph_alignment = "center"
                left_indent_pt = 55
                right_indent_pt = 55
                space_after_pt = 30
            }
        },
        @{
            type = "text"
            text = "Dokument utworzony bezpośrednio w prawdziwym Microsoft Wordzie przez natywny runtime .NET. Wszystkie elementy pozostają edytowalne."
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#666666"
                paragraph_alignment = "center"
                space_after_pt = 12
            }
        },
        @{
            type = "text"
            text = "Wersja kontrolna: $replaceMarker"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 9
                font_color_rgb = "#777777"
                paragraph_alignment = "center"
            }
        },
        (HeadingOperation -Text "Spis rozdziałów" -PageBreak $true -Size 24),
        (BodyOperation -Text "Poniższa lista jest natywną listą numerowaną Worda. Każdy rozdział rozpoczyna się na nowej stronie, dzięki czemu dokument ma stabilny, czytelny szkielet.")
    )
    $front = Invoke-Tool `
        -Name "apply_live_word_operations" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            operations = $frontOperations
        }
    $version = [long]$front.live_version

    $stage = "insert native contents list"
    $contentsList = Invoke-Tool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @($chapters | ForEach-Object { $_.title })
            list_kind = "numbered"
            target = "document_end"
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#17365D"
            }
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$contentsList.live_version

    $stage = "generate twenty full chapters"
    $bodyOperations = [Collections.Generic.List[object]]::new()
    foreach ($chapter in $chapters) {
        $bodyOperations.Add(
            (HeadingOperation -Text $chapter.title -PageBreak $true -Size 20)
        )
        $bodyOperations.Add((BodyOperation -Text $chapter.lead -Lead $true))
        foreach ($paragraph in $chapter.paragraphs) {
            $bodyOperations.Add((BodyOperation -Text $paragraph))
        }
    }
    Assert-True `
        -Condition ($bodyOperations.Count -le 200) `
        -Message "Body operation limit exceeded"
    $bodyWatch = [Diagnostics.Stopwatch]::StartNew()
    $body = Invoke-Tool `
        -Name "apply_live_word_operations" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            operations = @($bodyOperations)
        }
    $bodyWatch.Stop()
    $version = [long]$body.live_version
    $report.long_body_operations = $body.operation_count
    $report.long_body_ms = [Math]::Round($bodyWatch.Elapsed.TotalMilliseconds, 3)

    $stage = "replace draft marker"
    $replaced = Invoke-Tool `
        -Name "replace_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $replaceMarker
            replacement_text = $replacementMarker
            match_case = $true
            whole_word = $true
            replace_all = $true
            track_changes = "preserve"
            max_replacements = 5
            optimize_screen_updates = $true
            expected_version = $version
        }
    $version = [long]$replaced.live_version
    Assert-True `
        -Condition ($replaced.replacements -ge 1) `
        -Message "Draft marker replacement failed"
    $replacementReadback = Invoke-Tool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $replacementMarker
            match_case = $true
            whole_word = $true
            context_chars = 30
            max_results = 5
        }
    Assert-True `
        -Condition ($replacementReadback.match_count -ge 1) `
        -Message "Replacement marker readback failed"

    $stage = "scan installed object model"
    $types = Invoke-Tool `
        -Name "inspect_live_word_object_model_types" `
        -Arguments @{
            query = ""
            limit = 10
            refresh = $true
        }
    Assert-True `
        -Condition (
            $types.stats.type_count -gt 0 -and
            $types.stats.member_count -gt 0 -and
            $types.stats.scan_errors -eq 0
        ) `
        -Message "Installed Word type library scan failed"

    $stage = "inspect Range members"
    $members = Invoke-Tool `
        -Name "inspect_live_word_object_model_members" `
        -Arguments @{
            type_name = "Range"
            query = "Select"
            limit = 20
        }
    Assert-True `
        -Condition ($members.members.Count -ge 1) `
        -Message "Range.Select metadata is absent"

    $stage = "derive selection capabilities"
    $selectCaps = Invoke-Tool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "Range"
            query = "Select"
            member_kind = "method"
            execution = "write_allowed"
            limit = 20
        }
    $selectCap = @(
        $selectCaps.capabilities |
            Where-Object { $_.member.name -eq "Select" }
    )[0]
    $rangeCaps = Invoke-Tool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "_Document"
            query = "Range"
            member_kind = "method"
            execution = "read_allowed"
            limit = 20
        }
    $rangeCap = @(
        $rangeCaps.capabilities |
            Where-Object { $_.member.name -eq "Range" }
    )[0]
    Assert-True `
        -Condition (
            [bool]$selectCap.capability_id -and
            [bool]$rangeCap.capability_id
        ) `
        -Message "Safe selection capabilities are absent"

    $selectionOperations = @(
        @{
            operation_id = "create_title_range"
            capability_id = $rangeCap.capability_id
            target = @{ kind = "document" }
            arguments = @(0, 20)
            result_id = "title_range"
        },
        @{
            operation_id = "select_title_range"
            capability_id = $selectCap.capability_id
            target = @{
                kind = "result"
                result_id = "title_range"
            }
            arguments = @()
        }
    )
    $stage = "preflight title selection"
    $selectionPreflight = Invoke-Tool `
        -Name "preflight_live_word_member_operations" `
        -Arguments @{ operations = $selectionOperations }
    Assert-True `
        -Condition (
            $selectionPreflight.valid -and
            $selectionPreflight.mutating_count -eq 0
        ) `
        -Message "Title selection preflight failed"

    $stage = "select title through catalog"
    $selectionExecution = Invoke-Tool `
        -Name "execute_live_word_member_operations" `
        -Arguments @{
            live_document_id = $documentId
            operations = $selectionOperations
            activate = $true
        }
    Assert-True `
        -Condition ($selectionExecution.executed_count -eq 2) `
        -Message "Title selection execution failed"

    $stage = "read selected title"
    $selection = Invoke-Tool `
        -Name "get_live_word_selection" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition (-not $selection.selection.collapsed) `
        -Message "Title selection is empty"

    $stage = "format selected title"
    $formatted = Invoke-Tool `
        -Name "format_live_word_selection" `
        -Arguments @{
            live_document_id = $documentId
            selection_token = $selection.selection.selection_token
            expected_version = $version
            formatting = @{
                bold = $true
                underline = $true
                font_color_rgb = "#17365D"
            }
        }
    $version = [long]$formatted.live_version

    $stage = "find comment marker"
    $foundComment = Invoke-Tool `
        -Name "find_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            search_text = $commentMarker
            match_case = $true
            whole_word = $true
            context_chars = 40
            max_results = 2
        }
    Assert-True `
        -Condition (
            $foundComment.match_count -eq 1 -and
            [bool]$foundComment.matches[0].range_token
        ) `
        -Message "Comment marker was not found exactly once"

    $stage = "insert native comment"
    $comment = Invoke-Tool `
        -Name "insert_live_word_comment" `
        -Arguments @{
            live_document_id = $documentId
            range_token = $foundComment.matches[0].range_token
            text = "Komentarz demonstracyjny: ten zakres został znaleziony przez natywne Word Find i zabezpieczony tokenem treści."
            expected_version = $version
        }
    $version = [long]$comment.live_version

    $stage = "inspect comment review"
    $comments = Invoke-Tool `
        -Name "inspect_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            kind = "comments"
            include_text = $true
            max_text_chars = 300
            limit = 20
        }
    Assert-True `
        -Condition ($comments.total_count -ge 1) `
        -Message "Native comment was not visible in review"

    $stage = "append list showcase heading"
    $listHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks A. Natywne listy Worda"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$listHeading.live_version

    $stage = "insert bullet list"
    $bulletList = Invoke-Tool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @(
                "Tekst i formatowanie bezpośrednie",
                "Style, sekcje i przepływ strony",
                "Tabele, pola, zakładki i odsyłacze",
                "Grafika, przypisy, komentarze i równania",
                "Walidacja DOCX i eksport przez Word do PDF"
            )
            list_kind = "bullet"
            target = "document_end"
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10.5
            }
            expected_version = $version
        }
    $version = [long]$bulletList.live_version

    $stage = "insert numbered workflow list"
    $numberedList = Invoke-Tool `
        -Name "insert_live_word_list" `
        -Arguments @{
            live_document_id = $documentId
            items = @(
                "Rozpoznaj dokument i jego aktualną wersję.",
                "Wykonaj preflight danych i ograniczeń.",
                "Zastosuj jedną transakcję natywną.",
                "Zweryfikuj liczniki i strukturę Worda.",
                "Zapisz, waliduj, eksportuj i ponownie otwórz."
            )
            list_kind = "numbered"
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$numberedList.live_version

    $stage = "append table heading"
    $tableHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks B. Tabela danych z natywnymi formułami"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$tableHeading.live_version

    $stage = "insert formula table"
    $table = Invoke-Tool `
        -Name "insert_live_word_table" `
        -Arguments @{
            live_document_id = $documentId
            rows = @(
                @("Moduł", "Q1", "Q2", "Q3", "Q4", "Razem"),
                @("Tekst", "120", "180", "240", "300", ""),
                @("Tabele", "12", "18", "24", "30", ""),
                @("Pola", "8", "12", "16", "20", ""),
                @("Równania", "10", "15", "20", "25", ""),
                @("Recenzja", "6", "9", "12", "15", ""),
                @("Suma", "", "", "", "", "")
            )
            target = "document_end"
            header_row = $true
            autofit = "window"
            alignment = "center"
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$table.live_version

    $tableFormulas = @(
        @{
            row = 2
            column = 6
            function = "sum"
            directions = @("left")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 3
            column = 6
            function = "sum"
            directions = @("left")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 4
            column = 6
            function = "sum"
            directions = @("left")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 5
            column = 6
            function = "sum"
            directions = @("left")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 6
            column = 6
            function = "sum"
            directions = @("left")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 7
            column = 2
            function = "sum"
            directions = @("above")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 7
            column = 3
            function = "sum"
            directions = @("above")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 7
            column = 4
            function = "sum"
            directions = @("above")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 7
            column = 5
            function = "sum"
            directions = @("above")
            numeric_format = "0"
            replace_existing = $false
        },
        @{
            row = 7
            column = 6
            function = "sum"
            cell_range = @{
                start = @{ row = 2; column = 6 }
                end = @{ row = 6; column = 6 }
            }
            numeric_format = "0"
            replace_existing = $false
        }
    )

    $stage = "preflight table formulas"
    $formulaPreflight = Invoke-Tool `
        -Name "preflight_live_word_table_formulas" `
        -Arguments @{ formulas = $tableFormulas }
    Assert-True `
        -Condition $formulaPreflight.valid `
        -Message "Table formula preflight failed"

    $stage = "insert table formulas"
    $formulaResult = Invoke-Tool `
        -Name "insert_live_word_table_formulas" `
        -Arguments @{
            live_document_id = $documentId
            table_index = 1
            formulas = $tableFormulas
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
            force_update = $true
        }
    $version = [long]$formulaResult.live_version

    $stage = "update table fields"
    $updatedTable = Invoke-Tool `
        -Name "update_live_word_table_fields" `
        -Arguments @{
            live_document_id = $documentId
            table_index = 1
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$updatedTable.live_version

    $stage = "append support matrix heading"
    $matrixHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks C. Macierz pokrycia funkcji Worda"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$matrixHeading.live_version

    $coverageRows = @(
        @("Obszar Worda", "Stan", "Mechanizm"),
        @("Tekst i akapity", "Obsługiwane", "Dedykowane narzędzia transakcyjne"),
        @("Formatowanie zaznaczenia", "Obsługiwane", "Świeży token zaznaczenia"),
        @("Find i Replace", "Obsługiwane", "Natywny Range.Find"),
        @("Listy punktowane i numerowane", "Obsługiwane", "Natywny ListFormat"),
        @("Tabele prostokątne", "Obsługiwane", "Range.ConvertToTable"),
        @("Formuły tabel", "Obsługiwane", "Typowane pola Formula"),
        @("Zakładki i REF", "Obsługiwane", "Bookmarks oraz bezpieczne Fields"),
        @("Pola dokumentu", "Obsługiwane", "Allowlista typów"),
        @("Obrazy w tekście", "Obsługiwane", "InlineShapes"),
        @("Komentarze", "Obsługiwane", "Token zakresu i Comments"),
        @("Śledzenie zmian", "Obsługiwane", "TrackRevisions i tokeny rewizji"),
        @("Przypisy dolne i końcowe", "Obsługiwane", "Footnotes i Endnotes"),
        @("Nagłówki i stopki", "Obsługiwane częściowo", "Warianty sekcji i tekst"),
        @("Równania Office Math", "Obsługiwane", "LaTeX/UnicodeMath/MathML/OMML do OMath"),
        @("Walidacja DOCX", "Obsługiwane", "Microsoft Open XML SDK"),
        @("Eksport PDF", "Obsługiwane", "Natywny renderer Worda"),
        @("Style i motywy", "Częściowo", "Przypisanie stylu i katalog COM"),
        @("Sekcje i PageSetup", "Katalogowane", "Brak ogólnego edytora mutacji"),
        @("Kształty i pola tekstowe", "Katalogowane", "Wymagają dedykowanego edytora"),
        @("SmartArt i wykresy", "Katalogowane", "Złożone dane osadzone"),
        @("Kontrolki zawartości", "Inspekcja", "Mapa struktur"),
        @("Korespondencja seryjna", "Zablokowane", "Dane i skutki zewnętrzne"),
        @("Makra i DDE", "Zablokowane", "Ryzyko wykonania kodu"),
        @("Drukowanie i poczta", "Zablokowane", "Skutki zewnętrzne"),
        @("Hasła i ochrona globalna", "Zablokowane", "Skutek wrażliwy"),
        @("Nieudokumentowany interfejs", "Poza gwarancją", "Brak stabilnego kontraktu")
    )
    $coverageTable = Invoke-Tool `
        -Name "insert_live_word_table" `
        -Arguments @{
            live_document_id = $documentId
            rows = $coverageRows
            target = "document_end"
            header_row = $true
            autofit = "window"
            alignment = "left"
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$coverageTable.live_version

    $stage = "append bookmark heading"
    $bookmarkHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks D. Zakładki i pola dynamiczne"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$bookmarkHeading.live_version

    $bookmarkBatch = @(
        @{
            name = $bookmarkName
            text = "Punkt kontrolny atlasu: publiczny model Worda jest katalogowany, a wykonanie pozostaje ograniczone polityką."
            prefix_text = ""
            suffix_text = ""
            as_new_paragraph = $true
            formatting = @{
                bold = $true
                font_color_rgb = "#2457A6"
            }
        },
        @{
            name = "WTAtlasEquation_$suffix"
            text = "Sekcja równań natywnych znajduje się w następnym aneksie."
            prefix_text = ""
            suffix_text = ""
            as_new_paragraph = $true
            formatting = @{
                italic = $true
            }
        }
    )

    $stage = "preflight bookmarks"
    $bookmarkPreflight = Invoke-Tool `
        -Name "preflight_live_word_bookmarks" `
        -Arguments @{ bookmarks = $bookmarkBatch }
    Assert-True `
        -Condition $bookmarkPreflight.valid `
        -Message "Bookmark preflight failed"

    $stage = "insert bookmarks"
    $bookmarks = Invoke-Tool `
        -Name "insert_live_word_bookmarks" `
        -Arguments @{
            live_document_id = $documentId
            bookmarks = $bookmarkBatch
            target = "document_end"
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$bookmarks.live_version

    $fieldBatch = @(
        @{
            kind = "reference"
            bookmark = $bookmarkName
            hyperlink = $true
            prefix_text = "Odsyłacz do punktu kontrolnego: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "sequence"
            identifier = "WTATLAS"
            restart_at = 1
            prefix_text = "Natywny numer sekwencji: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "page"
            prefix_text = "Bieżąca strona: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "num_pages"
            prefix_text = "Liczba stron: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "section"
            prefix_text = "Bieżąca sekcja: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "section_pages"
            prefix_text = "Strony w sekcji: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "date"
            date_format = "yyyy-MM-dd"
            prefix_text = "Data: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "time"
            date_format = "HH:mm"
            prefix_text = "Czas: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "create_date"
            date_format = "yyyy-MM-dd HH:mm"
            prefix_text = "Utworzono: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "save_date"
            date_format = "yyyy-MM-dd HH:mm"
            prefix_text = "Ostatni zapis: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "file_name"
            include_path = $false
            prefix_text = "Nazwa pliku: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "word_count"
            prefix_text = "Liczba słów według Worda: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "character_count"
            prefix_text = "Liczba znaków według Worda: "
            suffix_text = ""
            as_new_paragraph = $true
        },
        @{
            kind = "formula"
            expression = "SUM(12,18,30)"
            numeric_format = "0"
            prefix_text = "Bezpieczna formuła kontrolna 12+18+30 = "
            suffix_text = ""
            as_new_paragraph = $true
        }
    )

    $stage = "preflight fields"
    $fieldPreflight = Invoke-Tool `
        -Name "preflight_live_word_fields" `
        -Arguments @{ fields = $fieldBatch }
    Assert-True `
        -Condition $fieldPreflight.valid `
        -Message "Field preflight failed"

    $stage = "insert fields"
    $fields = Invoke-Tool `
        -Name "insert_live_word_fields" `
        -Arguments @{
            live_document_id = $documentId
            fields = $fieldBatch
            target = "document_end"
            expected_version = $version
            activate = $true
            optimize_screen_updates = $true
        }
    $version = [long]$fields.live_version

    $stage = "append image heading"
    $imageHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks E. Grafika osadzona i opis alternatywny"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$imageHeading.live_version

    $stage = "insert inline image"
    $image = Invoke-Tool `
        -Name "insert_live_word_image" `
        -Arguments @{
            live_document_id = $documentId
            file_path = $sampleImage
            target = "document_end"
            width_points = 330
            lock_aspect_ratio = $true
            alternative_text = "Przykładowy wykres osadzony natywnie przez WordToolkit w demonstratorze funkcji Microsoft Word."
            title = "WordToolkit Atlas — grafika demonstracyjna"
            expected_version = $version
        }
    $version = [long]$image.live_version

    $stage = "append image caption"
    $caption = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Rysunek 1. Obraz został osadzony jako natywny InlineShape i posiada tekst alternatywny."
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 9
                italic = $true
                font_color_rgb = "#555555"
                paragraph_alignment = "center"
                space_before_pt = 6
            }
            expected_version = $version
        }
    $version = [long]$caption.live_version

    $stage = "append equation heading"
    $equationHeading = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Aneks F. Edytowalne równania Office Math"
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos Display"
                font_size_pt = 20
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "left"
                page_break_before = $true
                keep_with_next = $true
            }
            expected_version = $version
        }
    $version = [long]$equationHeading.live_version

    $mathMl = @'
<math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mn>1</mn><mrow><mi>x</mi><mo>+</mo><mn>1</mn></mrow></mfrac></math>
'@
    $omml = @'
<m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><m:rad><m:radPr><m:degHide m:val="1"/></m:radPr><m:deg/><m:e><m:r><m:t>x+1</m:t></m:r></m:e></m:rad></m:oMath>
'@
    $equationBatch = @(
        @{
            value = "\frac{x^2+1}{\sqrt{y}}"
            input_format = "latex"
            display = $true
        },
        @{
            value = "∑_(i=1)^n i^2=(n(n+1)(2n+1))/6"
            input_format = "unicodemath"
            display = $true
        },
        @{
            value = $mathMl
            input_format = "mathml"
            display = $true
        },
        @{
            value = $omml
            input_format = "omml"
            display = $true
        }
    )

    $stage = "preflight equations"
    $equationPreflight = Invoke-Tool `
        -Name "preflight_live_word_equations" `
        -Arguments @{ equations = $equationBatch }
    Assert-True `
        -Condition (
            $equationPreflight.valid -and
            $equationPreflight.equation_count -eq 4
        ) `
        -Message "Equation preflight failed"

    $stage = "insert single derivative equation"
    $singleEquation = Invoke-Tool `
        -Name "insert_live_word_equation" `
        -Arguments @{
            live_document_id = $documentId
            value = "f'(x)=3x^2"
            input_format = "latex"
            display = $true
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$singleEquation.live_version

    $stage = "insert equation format batch"
    $equations = Invoke-Tool `
        -Name "insert_live_word_equations_batch" `
        -Arguments @{
            live_document_id = $documentId
            equations = $equationBatch
            expected_version = $version
        }
    $version = [long]$equations.live_version

    $stage = "append footnote anchor"
    $footnoteAnchor = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "`nZdanie demonstracyjne prowadzące do natywnego przypisu dolnego."
            target = "document_end"
            as_new_paragraph = $false
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#444444"
                paragraph_alignment = "left"
                space_before_pt = 10
            }
            expected_version = $version
        }
    $version = [long]$footnoteAnchor.live_version

    $stage = "insert footnote"
    $footnote = Invoke-Tool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "footnote"
            text = "Przypis dolny utworzony przez natywną kolekcję Footnotes w prawdziwym Microsoft Wordzie."
            target = "document_end"
            expected_version = $version
        }
    $version = [long]$footnote.live_version

    $stage = "append endnote anchor"
    $endnoteAnchor = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "`nZdanie demonstracyjne prowadzące do natywnego przypisu końcowego."
            target = "document_end"
            as_new_paragraph = $false
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#444444"
                paragraph_alignment = "left"
                space_after_pt = 10
            }
            expected_version = $version
        }
    $version = [long]$endnoteAnchor.live_version

    $stage = "insert endnote"
    $endnote = Invoke-Tool `
        -Name "insert_live_word_note" `
        -Arguments @{
            live_document_id = $documentId
            kind = "endnote"
            text = "Przypis końcowy atlasu. Powierzchnia Worda jest szersza niż bezpieczna powierzchnia automatyzacji."
            target = "document_end"
            custom_mark = "A"
            expected_version = $version
        }
    $version = [long]$endnote.live_version

    $stage = "enable track changes"
    $trackOn = Invoke-Tool `
        -Name "manage_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            action = "set_track_changes"
            tracking_enabled = $true
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$trackOn.live_version

    $stage = "insert tracked statement"
    $trackedText = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "Wpis kontrolny recenzji: ten akapit został utworzony przy aktywnym śledzeniu zmian i pozostaje rewizją do świadomej decyzji użytkownika."
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#9C1C1C"
                italic = $true
            }
            expected_version = $version
        }
    $version = [long]$trackedText.live_version

    $stage = "inspect revisions"
    $revisions = Invoke-Tool `
        -Name "inspect_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            kind = "revisions"
            include_text = $true
            max_text_chars = 300
            limit = 50
        }
    Assert-True `
        -Condition ($revisions.total_count -ge 1) `
        -Message "Tracked insertion did not create a revision"

    $stage = "disable track changes"
    $trackOff = Invoke-Tool `
        -Name "manage_live_word_review" `
        -Arguments @{
            live_document_id = $documentId
            action = "set_track_changes"
            tracking_enabled = $false
            expected_version = $version
            optimize_screen_updates = $true
        }
    $version = [long]$trackOff.live_version

    $headerFooterCases = @(
        @{
            kind = "header"
            variant = "primary"
            text = "WORDTOOLKIT 0.18 · ATLAS MICROSOFT WORD"
            alignment = "center"
        },
        @{
            kind = "footer"
            variant = "primary"
            text = "Natywny dokument demonstracyjny · DOCX + PDF"
            alignment = "right"
        },
        @{
            kind = "header"
            variant = "first_page"
            text = "Pełny demonstrator funkcji dokumentowych"
            alignment = "left"
        },
        @{
            kind = "footer"
            variant = "even_pages"
            text = "WordToolkit · publiczny model Worda, bezpieczne wykonanie"
            alignment = "left"
        }
    )
    foreach ($case in $headerFooterCases) {
        $stage = "set $($case.kind) $($case.variant)"
        $headerFooter = Invoke-Tool `
            -Name "set_live_word_header_footer" `
            -Arguments @{
                live_document_id = $documentId
                section_index = 1
                kind = $case.kind
                variant = $case.variant
                text = $case.text
                enabled = $true
                link_to_previous = $false
                formatting = @{
                    font_name = "Aptos"
                    font_size_pt = 8
                    font_color_rgb = "#666666"
                    paragraph_alignment = $case.alignment
                }
                expected_version = $version
            }
        $version = [long]$headerFooter.live_version
    }

    $stage = "create and undo temporary probe"
    $undoProbe = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = "WT_ATLAS_UNDO_PROBE_$suffix"
            target = "document_end"
            as_new_paragraph = $true
            expected_version = $version
        }
    $version = [long]$undoProbe.live_version
    $undoState = Invoke-Tool `
        -Name "inspect_live_word_undo" `
        -Arguments @{
            live_document_id = $documentId
            max_entries = 5
        }
    Assert-True `
        -Condition $undoState.wordtoolkit_undo_eligible `
        -Message "Undo probe is not eligible"
    $undone = Invoke-Tool `
        -Name "undo_live_word_operation" `
        -Arguments @{
            live_document_id = $documentId
            undo_token = $undoState.undo_token
            expected_version = $version
        }
    $version = [long]$undone.live_version

    $stage = "map structures"
    $structureMap = Invoke-Tool `
        -Name "map_live_word_structures" `
        -Arguments @{
            live_document_id = $documentId
            include_type_histograms = $true
            adaptive_type_histograms = $true
            max_type_items = 1000
        }
    Assert-True `
        -Condition (
            $structureMap.inspectable_structures.Count -eq 23 -and
            $structureMap.structures.tables -ge 2 -and
            $structureMap.structures.bookmarks -ge 2 -and
            $structureMap.structures.fields -ge 10
        ) `
        -Message "Atlas structure map is incomplete"

    $stage = "inspect bookmark structures"
    $structureItems = Invoke-Tool `
        -Name "inspect_live_word_structure_items" `
        -Arguments @{
            live_document_id = $documentId
            structure = "bookmarks"
            limit = 50
            include_text = $false
            adaptive_property_probing = $true
        }
    Assert-True `
        -Condition ($structureItems.returned_count -ge 2) `
        -Message "Bookmark structure inspection failed"

    $stage = "inspect equation learning"
    $equationLearning = Invoke-Tool `
        -Name "inspect_live_word_equation_learning" `
        -Arguments @{}
    Assert-True `
        -Condition ($equationLearning.observation_count -ge 5) `
        -Message "Equation outcome counters are incomplete"

    $stage = "inspect structure learning"
    $structureLearning = Invoke-Tool `
        -Name "inspect_live_word_structure_learning" `
        -Arguments @{}
    Assert-True `
        -Condition ($structureLearning.observation_count -ge 23) `
        -Message "Structure learning counters are incomplete"

    $stage = "diagnose layout"
    $layout = Invoke-Tool `
        -Name "diagnose_live_word_layout" `
        -Arguments @{
            live_document_id = $documentId
            max_paragraphs = 5000
            max_issues = 500
            keep_with_next_threshold = 5
            long_heading_chars = 100
            long_keep_together_chars = 1200
        }
    Assert-True `
        -Condition ($layout.scanned_paragraphs -ge 100) `
        -Message "Layout diagnosis scanned too little content"

    $stage = "append native audit summary"
    $auditText = @"
Audyt natywny: katalog zainstalowanego Worda wykrył $($types.stats.type_count) typów i $($types.stats.member_count) elementów publicznego modelu obiektowego. Dokument zawiera $($structureMap.structures.tables) tabel, $($structureMap.structures.fields) pól, $($structureMap.structures.bookmarks) zakładek, $($structureMap.structures.comments) komentarzy i $($structureMap.structures.equations) równań. Diagnoza układu przeskanowała $($layout.scanned_paragraphs) akapitów. Te liczby pochodzą z żywego Worda, nie z deklaracji zapisanej w tekście.
"@
    $audit = Invoke-Tool `
        -Name "insert_live_word_text" `
        -Arguments @{
            live_document_id = $documentId
            text = $auditText.Trim()
            target = "document_end"
            as_new_paragraph = $true
            formatting = @{
                font_name = "Aptos"
                font_size_pt = 10
                font_color_rgb = "#17365D"
                bold = $true
                paragraph_alignment = "justify"
                space_before_pt = 12
                space_after_pt = 12
                keep_together = $true
            }
            expected_version = $version
        }
    $version = [long]$audit.live_version

    $stage = "derive final field refresh capabilities"
    $documentFieldsCaps = Invoke-Tool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "_Document"
            query = "Fields"
            member_kind = "property_get"
            execution = "read_allowed"
            limit = 20
        }
    $documentFieldsCap = @(
        $documentFieldsCaps.capabilities |
            Where-Object { $_.member.name -eq "Fields" }
    )[0]
    $fieldsUpdateCaps = Invoke-Tool `
        -Name "inspect_live_word_member_capabilities" `
        -Arguments @{
            type_name = "Fields"
            query = "Update"
            member_kind = "method"
            execution = "write_allowed"
            limit = 20
        }
    $fieldsUpdateCap = @(
        $fieldsUpdateCaps.capabilities |
            Where-Object { $_.member.name -eq "Update" }
    )[0]
    Assert-True `
        -Condition (
            [bool]$documentFieldsCap.capability_id -and
            [bool]$fieldsUpdateCap.capability_id
        ) `
        -Message "Final document field refresh capabilities are absent"

    $fieldRefreshOperations = @(
        @{
            operation_id = "get_final_document_fields"
            capability_id = $documentFieldsCap.capability_id
            target = @{ kind = "document" }
            arguments = @()
            result_id = "final_document_fields"
        },
        @{
            operation_id = "update_final_document_fields"
            capability_id = $fieldsUpdateCap.capability_id
            target = @{
                kind = "result"
                result_id = "final_document_fields"
            }
            arguments = @()
        }
    )
    $stage = "preflight final field refresh"
    $fieldRefreshPreflight = Invoke-Tool `
        -Name "preflight_live_word_member_operations" `
        -Arguments @{ operations = $fieldRefreshOperations }
    Assert-True `
        -Condition (
            $fieldRefreshPreflight.valid -and
            $fieldRefreshPreflight.mutating_count -eq 1
        ) `
        -Message "Final document field refresh preflight failed"

    $stage = "refresh final document fields"
    $fieldRefresh = Invoke-Tool `
        -Name "execute_live_word_member_operations" `
        -Arguments @{
            live_document_id = $documentId
            operations = $fieldRefreshOperations
            expected_version = $version
            activate = $true
            optimize_screen_updates = $false
        }
    $version = [long]$fieldRefresh.live_version

    $stage = "save atlas"
    $saved = Invoke-Tool `
        -Name "save_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
        }
    Assert-True -Condition $saved.saved -Message "Atlas was not saved"

    $stage = "validate atlas"
    $validated = Invoke-Tool `
        -Name "validate_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition $validated.validation.valid `
        -Message "Atlas failed Open XML SDK validation"

    $stage = "export atlas PDF"
    $pdf = Invoke-Tool `
        -Name "export_live_word_pdf" `
        -Arguments @{
            live_document_id = $documentId
            output_path = $pdfPath
            overwrite = $false
            optimize_for = "print"
            bookmarks = "headings"
            include_document_properties = $true
            pdf_a = $false
        }
    Assert-True `
        -Condition ($pdf.exported -and $pdf.bytes -gt 0) `
        -Message "Atlas PDF is empty"

    $stage = "inspect final structures"
    $beforeClose = Invoke-Tool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition (
            $beforeClose.document.paragraph_count -ge 100 -and
            $beforeClose.document.table_count -ge 2 -and
            $beforeClose.document.comment_count -ge 1 -and
            $beforeClose.document.footnote_count -ge 1 -and
            $beforeClose.document.endnote_count -ge 1 -and
            $beforeClose.document.inline_image_count -ge 1 -and
            $beforeClose.document.equation_count -ge 5
        ) `
        -Message "Atlas is missing one or more native structures"

    $stage = "close atlas"
    [void](Invoke-Tool `
        -Name "close_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            save_changes = "save"
            expected_version = $version
        })
    $documentId = ""
    $documentOpen = $false

    $stage = "open saved atlas"
    $opened = Invoke-Tool `
        -Name "open_live_word_document" `
        -Arguments @{
            file_path = $documentPath
            read_only = $false
            activate = $true
            visible = $true
            add_to_recent_files = $false
            open_and_repair = $false
            launch_if_needed = $false
        }
    $documentId = $opened.live_document_id
    $version = [long]$opened.live_version
    $documentOpen = $true
    Assert-True `
        -Condition (
            $opened.document.paragraph_count -ge 100 -and
            $opened.document.table_count -ge 2 -and
            $opened.document.comment_count -ge 1 -and
            $opened.document.footnote_count -ge 1 -and
            $opened.document.endnote_count -ge 1 -and
            $opened.document.inline_image_count -ge 1 -and
            $opened.document.equation_count -ge 5
        ) `
        -Message "Reopened atlas lost native structures"

    $stage = "disconnect open atlas"
    [void](Invoke-Tool `
        -Name "disconnect_live_word_document" `
        -Arguments @{ live_document_id = $documentId })
    $documentId = ""
    $documentOpen = $false

    $stage = "connect atlas by exact path"
    $connected = Invoke-Tool `
        -Name "connect_live_word_document" `
        -Arguments @{
            full_path = $documentPath
            use_active = $false
            activate = $true
        }
    $documentId = $connected.live_document_id
    $version = [long]$connected.live_version
    $documentOpen = $true

    $stage = "inspect exact reconnect"
    $finalInspection = Invoke-Tool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition ($finalInspection.document.full_name -eq $documentPath) `
        -Message "Exact reconnect targeted the wrong document"

    $stage = "refresh pagination-dependent fields after reopen"
    $finalFieldRefresh = Invoke-Tool `
        -Name "execute_live_word_member_operations" `
        -Arguments @{
            live_document_id = $documentId
            operations = $fieldRefreshOperations
            expected_version = $version
            activate = $true
            optimize_screen_updates = $false
        }
    $version = [long]$finalFieldRefresh.live_version

    $stage = "save final refreshed fields"
    $finalSaved = Invoke-Tool `
        -Name "save_live_word_document" `
        -Arguments @{
            live_document_id = $documentId
            expected_version = $version
        }
    Assert-True `
        -Condition $finalSaved.saved `
        -Message "Final refreshed fields were not saved"

    $stage = "validate final refreshed atlas"
    $validated = Invoke-Tool `
        -Name "validate_live_word_document" `
        -Arguments @{ live_document_id = $documentId }
    Assert-True `
        -Condition $validated.validation.valid `
        -Message "Final refreshed atlas failed Open XML SDK validation"

    $stage = "replace PDF after final pagination refresh"
    $pdf = Invoke-Tool `
        -Name "export_live_word_pdf" `
        -Arguments @{
            live_document_id = $documentId
            output_path = $pdfPath
            overwrite = $true
            optimize_for = "print"
            bookmarks = "headings"
            include_document_properties = $true
            pdf_a = $false
        }
    Assert-True `
        -Condition ($pdf.exported -and $pdf.bytes -gt 0) `
        -Message "Final refreshed atlas PDF is empty"

    $stage = "inspect final refreshed atlas"
    $finalInspection = Invoke-Tool `
        -Name "inspect_live_word_document" `
        -Arguments @{ live_document_id = $documentId }

    $stage = "release handle and leave atlas open"
    [void](Invoke-Tool `
        -Name "disconnect_live_word_document" `
        -Arguments @{ live_document_id = $documentId })
    $documentId = ""
    $documentOpen = $false

    $totalWatch.Stop()
    $report.passed = $true
    $report.total_seconds = [Math]::Round($totalWatch.Elapsed.TotalSeconds, 3)
    $report.total_mcp_requests = $requestId
    $report.tool_calls = $toolCalls
    $report.exposed_tool_count = 14
    $report.available_action_count = 82
    $report.object_model_types = $types.stats.type_count
    $report.object_model_members = $types.stats.member_count
    $report.paragraphs = $finalInspection.document.paragraph_count
    $report.tables = $finalInspection.document.table_count
    $report.fields = $structureMap.structures.fields
    $report.bookmarks = $structureMap.structures.bookmarks
    $report.comments = $finalInspection.document.comment_count
    $report.revisions = $revisions.total_count
    $report.footnotes = $finalInspection.document.footnote_count
    $report.endnotes = $finalInspection.document.endnote_count
    $report.inline_images = $finalInspection.document.inline_image_count
    $report.equations = $finalInspection.document.equation_count
    $report.sections = $finalInspection.document.section_count
    $report.pdf_bytes = [long]$pdf.bytes
    $report.openxml_valid = $validated.validation.valid
    $report.close_open_reconnect_passed = $true
    $report.left_open_in_word = $true
    $report.slowest_tools = @(
        $stageTimings |
            Sort-Object milliseconds -Descending |
            Select-Object -First 10
    )
}
catch {
    if ($totalWatch.IsRunning) {
        $totalWatch.Stop()
    }
    $failure = $_
    $report.passed = $false
    $report.total_seconds = [Math]::Round($totalWatch.Elapsed.TotalSeconds, 3)
    $report.failed_stage = $stage
    $report.error = $_.Exception.Message
    $report.total_mcp_requests = $requestId
    $report.tool_calls = $toolCalls
}
finally {
    if ($documentId -and $documentOpen) {
        try {
            [void](Invoke-Tool `
                -Name "disconnect_live_word_document" `
                -Arguments @{ live_document_id = $documentId })
        }
        catch {
            # Preserve the first failure and do not close the user's Word process.
        }
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) {
        $process.Kill($true)
    }
}

$report | ConvertTo-Json -Depth 50
if ($failure) {
    exit 1
}
