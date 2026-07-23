from __future__ import annotations

import argparse
import asyncio
import json
import os
import shutil
from pathlib import Path
from urllib.parse import unquote, urlparse

from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

DEFAULT_PLUGIN = Path(r"C:\Users\Admin\plugins\wordtoolkit")
DEFAULT_OUTPUT = Path(
    r"C:\Users\Admin\Desktop\codex\WordToolKit"
    r"\Rownanie_Schrodingera_automatycznie.docx"
)


def payload(result) -> dict:
    if result.structuredContent:
        body = result.structuredContent
    else:
        body = None
        for item in result.content:
            text = getattr(item, "text", None)
            if not text:
                continue
            try:
                body = json.loads(text)
                break
            except json.JSONDecodeError:
                continue
    if body is None:
        raise RuntimeError("WordToolkit returned no structured payload")
    if not body.get("ok", False):
        raise RuntimeError(json.dumps(body.get("error", body), ensure_ascii=False))
    return body


def artifact_path(uri: str) -> Path:
    parsed = urlparse(uri)
    if parsed.scheme != "file":
        raise RuntimeError(f"Expected a local file URI, received: {uri}")
    value = unquote(parsed.path)
    if os.name == "nt" and value.startswith("/") and value[2:3] == ":":
        value = value[1:]
    return Path(value)


class DocumentAutomation:
    def __init__(self, session: ClientSession, document_id: str, version: int, anchor: str):
        self.session = session
        self.document_id = document_id
        self.version = version
        self.anchor = anchor
        self.heading_ids: list[str] = []
        self.equation_ids: list[str] = []

    async def read(self, tool: str, args: dict | None = None) -> dict:
        return payload(
            await self.session.call_tool(
                tool,
                {"document_id": self.document_id, **(args or {})},
            )
        )

    async def mutate(self, tool: str, args: dict | None = None) -> dict:
        result = payload(
            await self.session.call_tool(
                tool,
                {
                    "document_id": self.document_id,
                    **(args or {}),
                    "expected_version": self.version,
                },
            )
        )
        new_version = result["data"].get("draft_version")
        if new_version != self.version + 1:
            raise RuntimeError(
                f"{tool} returned draft version {new_version}; expected {self.version + 1}"
            )
        self.version = new_version
        return result

    async def create_style(self, name: str, **properties) -> None:
        await self.mutate("create_style", {"name": name, **properties})

    async def paragraph(self, text: str, style: str = "Toolkit Body") -> str:
        result = await self.mutate(
            "insert_paragraph",
            {
                "after_paragraph_id": self.anchor,
                "text": text,
                "style": style,
            },
        )
        paragraph_id = result["data"]["result"]["para_id"]
        self.anchor = paragraph_id
        return paragraph_id

    async def heading(self, text: str, level: int, *, page_break: bool = False) -> str:
        paragraph_id = await self.paragraph(text, f"Heading{level}")
        await self.mutate(
            "format_paragraph",
            {
                "paragraph_id": paragraph_id,
                "keep_with_next": True,
                "keep_lines_together": True,
                "widow_control": True,
                "page_break_before": page_break,
                "space_before_pt": 14 if level == 1 else 10,
                "space_after_pt": 5,
            },
        )
        self.heading_ids.append(paragraph_id)
        return paragraph_id

    async def equation(self, latex: str) -> str:
        result = await self.mutate(
            "insert_equation",
            {
                "anchor_paragraph_id": self.anchor,
                "value": latex,
                "input_format": "latex",
                "display": True,
                "position": "after",
            },
        )
        equation = result["data"]["result"]
        paragraph_id = equation["paragraph_id"]
        if not equation.get("display") or not paragraph_id:
            raise RuntimeError(f"Equation was not inserted as display Office Math: {latex}")
        self.anchor = paragraph_id
        self.equation_ids.append(equation["equation_id"])
        await self.mutate(
            "format_paragraph",
            {
                "paragraph_id": paragraph_id,
                "alignment": "center",
                "keep_lines_together": True,
                "widow_control": True,
                "space_before_pt": 3,
                "space_after_pt": 7,
            },
        )
        return paragraph_id

    async def list_items(self, items: list[str], *, numbered: bool = False) -> list[str]:
        paragraph_ids = []
        for item in items:
            paragraph_ids.append(await self.paragraph(item, "Toolkit Body"))
        await self.mutate(
            "manage_lists",
            {
                "action": "apply",
                "paragraph_ids": paragraph_ids,
                "list_style": "numbered" if numbered else "bullet",
                "start": 1,
            },
        )
        return paragraph_ids


async def build_document(plugin: Path, output: Path) -> dict:
    parameters = StdioServerParameters(
        command="uv",
        args=[
            "run",
            "--isolated",
            "--project",
            "./runtime",
            "--frozen",
            "wordtoolkit-stdio",
        ],
        cwd=plugin,
        env={
            **os.environ,
            "PYTHONDONTWRITEBYTECODE": "1",
            "WORDTOOLKIT_AUTH_MODE": "local_stdio",
            "PYTHONUTF8": "1",
            "VIRTUAL_ENV": "",
        },
    )

    async with stdio_client(parameters) as (read, write), ClientSession(read, write) as session:
        initialized = await session.initialize()
        tools = await session.list_tools()
        tool_names = {tool.name for tool in tools.tools}
        required = {
            "create_document",
            "create_style",
            "replace_paragraph",
            "insert_paragraph",
            "format_paragraph",
            "insert_equation",
            "manage_lists",
            "manage_headers_footers",
            "inspect_document",
            "list_equations",
            "validate_equations",
            "validate_ooxml",
            "check_accessibility",
            "check_layout_risks",
            "generate_preview",
            "export_document",
            "close_document",
        }
        missing = sorted(required - tool_names)
        if missing:
            raise RuntimeError(f"Installed WordToolkit is missing required tools: {missing}")

        created = payload(
            await session.call_tool(
                "create_document",
                {
                    "page_size": "A4",
                    "orientation": "portrait",
                    "margin_mm": 22,
                },
            )
        )
        data = created["data"]
        doc = DocumentAutomation(
            session,
            document_id=data["document_id"],
            version=data["draft_version"],
            anchor=data["anchor_paragraph_id"],
        )

        await doc.create_style(
            "Toolkit Title",
            based_on="Title",
            next_style="Toolkit Subtitle",
            font_name="Aptos Display",
            font_size_pt=28,
            font_color="17365D",
            bold=True,
            space_after_pt=12,
            line_spacing=1.0,
        )
        await doc.create_style(
            "Toolkit Subtitle",
            based_on="Subtitle",
            next_style="Toolkit Lead",
            font_name="Aptos",
            font_size_pt=13,
            font_color="44546A",
            italic=True,
            space_after_pt=16,
            line_spacing=1.1,
        )
        await doc.create_style(
            "Toolkit Lead",
            based_on="Normal",
            next_style="Toolkit Body",
            font_name="Aptos",
            font_size_pt=11.5,
            font_color="1F4E79",
            space_after_pt=12,
            line_spacing=1.15,
        )
        await doc.create_style(
            "Toolkit Body",
            based_on="Normal",
            next_style="Toolkit Body",
            font_name="Aptos",
            font_size_pt=10.5,
            font_color="222222",
            space_after_pt=6,
            line_spacing=1.15,
        )
        await doc.create_style(
            "Toolkit Note",
            based_on="Normal",
            next_style="Toolkit Body",
            font_name="Aptos",
            font_size_pt=10.5,
            font_color="1F4E79",
            bold=True,
            space_before_pt=6,
            space_after_pt=9,
            line_spacing=1.1,
        )
        await doc.create_style(
            "Toolkit Small",
            based_on="Normal",
            next_style="Toolkit Body",
            font_name="Aptos",
            font_size_pt=9,
            font_color="666666",
            italic=True,
            space_after_pt=4,
            line_spacing=1.0,
        )
        await doc.mutate(
            "update_style",
            {
                "name": "Heading1",
                "font_name": "Aptos Display",
                "font_size_pt": 18,
                "font_color": "17365D",
                "bold": True,
                "space_before_pt": 14,
                "space_after_pt": 5,
                "line_spacing": 1.0,
            },
        )
        await doc.mutate(
            "update_style",
            {
                "name": "Heading2",
                "font_name": "Aptos Display",
                "font_size_pt": 13.5,
                "font_color": "2F5597",
                "bold": True,
                "space_before_pt": 10,
                "space_after_pt": 4,
                "line_spacing": 1.0,
            },
        )

        title = await doc.mutate(
            "replace_paragraph",
            {
                "paragraph_id": doc.anchor,
                "text": "Nieskończona studnia potencjału",
                "style": "Toolkit Title",
            },
        )
        doc.anchor = title["data"]["result"]["para_id"]
        await doc.mutate(
            "format_paragraph",
            {
                "paragraph_id": doc.anchor,
                "alignment": "center",
                "keep_lines_together": True,
                "widow_control": True,
                "space_before_pt": 62,
                "space_after_pt": 12,
            },
        )
        subtitle_id = await doc.paragraph(
            "Pełne rozwiązanie równania Schrödingera krok po kroku",
            "Toolkit Subtitle",
        )
        await doc.mutate(
            "format_paragraph",
            {
                "paragraph_id": subtitle_id,
                "alignment": "center",
                "keep_lines_together": True,
                "widow_control": True,
            },
        )
        lead_id = await doc.paragraph(
            "Od równania różniczkowego i warunków brzegowych do skwantowanych "
            "poziomów energii, funkcji własnych i interpretacji probabilistycznej.",
            "Toolkit Lead",
        )
        await doc.mutate(
            "format_paragraph",
            {
                "paragraph_id": lead_id,
                "alignment": "center",
                "left_indent_mm": 18,
                "right_indent_mm": 18,
                "keep_lines_together": True,
                "widow_control": True,
            },
        )
        small_id = await doc.paragraph(
            "Dokument wygenerowany automatycznie przez WordToolkit. "
            "Wszystkie wzory są edytowalnymi obiektami Office Math.",
            "Toolkit Small",
        )
        await doc.mutate(
            "format_paragraph",
            {
                "paragraph_id": small_id,
                "alignment": "center",
                "keep_lines_together": True,
                "widow_control": True,
            },
        )

        await doc.heading("1. Problem i model", 1, page_break=True)
        await doc.paragraph(
            "Rozważamy cząstkę o masie m uwięzioną w jednowymiarowym obszarze "
            "od x = 0 do x = L. Wewnątrz tego przedziału potencjał jest równy zeru. "
            "Poza nim rośnie do nieskończoności, dlatego cząstka nie może wydostać się "
            "na zewnątrz."
        )
        await doc.equation(r"V(x)=0,\qquad 0<x<L")
        await doc.equation(r"V(x)\to\infty,\qquad x\notin(0,L)")
        await doc.paragraph(
            "Stan stacjonarny spełnia równanie własne hamiltonianu. Energia E jest "
            "wartością własną, a funkcja falowa ψ — odpowiadającą jej funkcją własną."
        )
        await doc.equation(r"\hat{H}\psi(x)=E\psi(x)")
        await doc.equation(
            r"-\frac{\hbar^2}{2m}\frac{d^2\psi(x)}{dx^2}"
            r"+V(x)\psi(x)=E\psi(x)"
        )
        await doc.paragraph(
            "Nieskończone ściany wymuszają znikanie funkcji falowej na obu końcach "
            "przedziału. To właśnie warunki brzegowe odrzucą prawie wszystkie możliwe "
            "energie."
        )
        await doc.equation(r"\psi(0)=0,\qquad \psi(L)=0")

        await doc.heading("2. Rozwiązanie równania wewnątrz studni", 1)
        await doc.heading("Krok 1 — redukcja do równania harmonicznego", 2)
        await doc.paragraph(
            "W przedziale 0 < x < L potencjał znika. Po przeniesieniu wszystkich "
            "wyrazów na jedną stronę otrzymujemy liniowe równanie różniczkowe drugiego "
            "rzędu o stałych współczynnikach."
        )
        await doc.equation(r"\frac{d^2\psi(x)}{dx^2}+k^2\psi(x)=0")
        await doc.equation(r"k^2=\frac{2mE}{\hbar^2}")
        await doc.heading("Krok 2 — rozwiązanie ogólne", 2)
        await doc.paragraph(
            "Dla dodatniej energii rozwiązaniem jest kombinacja funkcji sinus i cosinus."
        )
        await doc.equation(r"\psi(x)=A\sin(kx)+B\cos(kx)")
        await doc.heading("Krok 3 — pierwszy warunek brzegowy", 2)
        await doc.paragraph(
            "Podstawienie x = 0 zeruje sinus, natomiast cosinus przyjmuje wartość jeden. "
            "Jedyną możliwością spełnienia warunku jest więc B = 0."
        )
        await doc.equation(r"\psi(0)=0\quad\Rightarrow\quad B=0")
        await doc.equation(r"\psi(x)=A\sin(kx)")
        await doc.heading("Krok 4 — drugi warunek brzegowy i kwantowanie", 2)
        await doc.paragraph(
            "Dla x = L funkcja również musi zniknąć. Niezerowa funkcja falowa istnieje "
            "tylko wtedy, gdy argument sinusa jest całkowitą wielokrotnością liczby π."
        )
        await doc.equation(r"\psi(L)=0\quad\Rightarrow\quad \sin(kL)=0")
        await doc.equation(r"k_n=\frac{n\pi}{L},\qquad n=1,2,3,\ldots")
        await doc.paragraph(
            "Liczba n jest główną liczbą kwantową tego układu. Wartość n = 0 odpada, "
            "ponieważ dawałaby funkcję falową równą zeru w całym przedziale, a więc brak "
            "jakiegokolwiek stanu fizycznego."
        )

        await doc.heading("3. Widmo energii i normalizacja", 1)
        await doc.heading("Dozwolone energie", 2)
        await doc.paragraph(
            "Podstawiamy skwantowane wartości k do związku między k i energią. Otrzymujemy "
            "dyskretne widmo: cząstka nie może mieć dowolnej energii."
        )
        await doc.equation(r"E_n=\frac{n^2\pi^2\hbar^2}{2mL^2}")
        await doc.paragraph(
            "Energia rośnie jak kwadrat liczby kwantowej i maleje jak kwadrat szerokości "
            "studni. Dwukrotne zwężenie obszaru podnosi wszystkie energie czterokrotnie."
        )
        await doc.heading("Normalizacja funkcji falowej", 2)
        await doc.paragraph(
            "Całkowite prawdopodobieństwo znalezienia cząstki wewnątrz studni musi być "
            "równe jeden. Ten warunek ustala amplitudę A."
        )
        await doc.equation(r"\int_0^L\left|\psi_n(x)\right|^2\,dx=1")
        await doc.equation(
            r"1=\left|A_n\right|^2\int_0^L"
            r"\sin^2\left(\frac{n\pi x}{L}\right)\,dx"
            r"=\left|A_n\right|^2\frac{L}{2}"
        )
        await doc.equation(r"A_n=\sqrt{\frac{2}{L}}")
        await doc.paragraph("Ostateczna, znormalizowana rodzina funkcji własnych ma postać:")
        await doc.equation(
            r"\psi_n(x)=\sqrt{\frac{2}{L}}"
            r"\sin\left(\frac{n\pi x}{L}\right)"
        )
        await doc.paragraph(
            "Różne stany własne są ortogonalne. Dzięki temu dowolny stan w studni można "
            "rozłożyć na ich superpozycję."
        )
        await doc.equation(r"\int_0^L\psi_m^*(x)\psi_n(x)\,dx=\delta_{mn}")

        await doc.heading("4. Przykład liczbowy: elektron w studni 1 nm", 1)
        await doc.paragraph(
            "Przyjmijmy szerokość L = 1,00 nm i masę elektronu. Dla stałej Plancka "
            "zredukowanej ℏ = 1,054 571 817 × 10⁻³⁴ J·s otrzymujemy energię podstawową:"
        )
        await doc.equation(
            r"E_1=\frac{\pi^2\hbar^2}{2m_eL^2}"
            r"\approx 6.025\times10^{-20}\,\mathrm{J}"
        )
        await doc.equation(r"E_1\approx0.3760\,\mathrm{eV}")
        await doc.paragraph(
            "Kolejne poziomy są prostymi wielokrotnościami E₁ wynikającymi z czynnika n²:"
        )
        await doc.list_items(
            [
                "n = 1: E₁ ≈ 0,3760 eV — stan podstawowy.",
                "n = 2: E₂ = 4E₁ ≈ 1,5041 eV.",
                "n = 3: E₃ = 9E₁ ≈ 3,3843 eV.",
                "Przejście 1 → 2 wymaga energii ΔE ≈ 1,1281 eV.",
            ]
        )
        await doc.paragraph(
            "Stan podstawowy nie ma energii równej zeru. Gdyby jednocześnie położenie "
            "cząstki było ograniczone do studni i pęd był dokładnie równy zeru, naruszona "
            "zostałaby zasada nieoznaczoności."
        )
        await doc.equation(r"E_2-E_1=3E_1\approx1.1281\,\mathrm{eV}")

        await doc.heading("5. Interpretacja probabilistyczna", 1)
        await doc.heading("Gęstość prawdopodobieństwa", 2)
        await doc.paragraph(
            "Kwadrat modułu funkcji falowej opisuje gęstość prawdopodobieństwa. Węzły "
            "pojawiają się tam, gdzie funkcja falowa przechodzi przez zero."
        )
        await doc.equation(
            r"\rho_n(x)=\left|\psi_n(x)\right|^2"
            r"=\frac{2}{L}\sin^2\left(\frac{n\pi x}{L}\right)"
        )
        await doc.paragraph(
            "Symetria studni sprawia, że średnie położenie jest zawsze w jej środku, "
            "niezależnie od n."
        )
        await doc.equation(
            r"\langle x\rangle_n=\int_0^Lx\left|\psi_n(x)\right|^2\,dx"
            r"=\frac{L}{2}"
        )
        await doc.paragraph(
            "Niepewność położenia zależy od liczby kwantowej i dąży dla dużych n do "
            "klasycznej wartości rozkładu jednostajnego."
        )
        await doc.equation(r"\Delta x_n=L\sqrt{\frac{1}{12}-\frac{1}{2n^2\pi^2}}")
        await doc.heading("Prawdopodobieństwo w środkowej połowie studni", 2)
        await doc.paragraph(
            "Dla stanu podstawowego całkujemy gęstość od L/4 do 3L/4. Wynik pokazuje, "
            "że elektron najczęściej pojawia się w pobliżu środka, choć nie ma jednej "
            "ustalonej trajektorii."
        )
        await doc.equation(
            r"P=\int_{L/4}^{3L/4}\frac{2}{L}"
            r"\sin^2\left(\frac{\pi x}{L}\right)\,dx"
        )
        await doc.equation(r"P=\frac{1}{2}+\frac{1}{\pi}\approx0.8183")

        await doc.heading("6. Ewolucja w czasie i superpozycja", 1)
        await doc.paragraph(
            "Stan własny energii zmienia w czasie jedynie fazę. Sama gęstość "
            "prawdopodobieństwa pozostaje wtedy nieruchoma."
        )
        await doc.equation(r"\Psi_n(x,t)=\psi_n(x)\exp\left(-\frac{iE_nt}{\hbar}\right)")
        await doc.paragraph(
            "Najogólniejszy stan jest superpozycją stanów własnych. Współczynnik cₙ "
            "określa amplitudę prawdopodobieństwa pomiaru energii Eₙ."
        )
        await doc.equation(
            r"\Psi(x,t)=\sum_{n=1}^{\infty}c_n\psi_n(x)"
            r"\exp\left(-\frac{iE_nt}{\hbar}\right)"
        )
        await doc.equation(r"\sum_{n=1}^{\infty}\left|c_n\right|^2=1")

        await doc.heading("7. Kontrola rozwiązania", 1)
        await doc.paragraph("Dobre rozwiązanie powinno przejść cztery niezależne kontrole:")
        await doc.list_items(
            [
                "Warunki brzegowe: ψₙ(0) = ψₙ(L) = 0.",
                "Normalizacja: całka z |ψₙ|² po całej studni jest równa jeden.",
                "Równanie własne: działanie hamiltonianu zwraca Eₙψₙ.",
                "Wymiary fizyczne: energia ma jednostkę dżula, a ψ wymiar L⁻¹ᐟ².",
            ]
        )
        await doc.paragraph(
            "Jeśli którykolwiek z tych testów pęka, rozwiązanie nie jest „prawie dobre”. "
            "Jest błędne u podstaw i trzeba wrócić do miejsca, w którym zgubiono warunek "
            "brzegowy, współczynnik normalizacji albo jednostkę."
        )

        await doc.heading("8. Zadania do samodzielnego rozwiązania", 1)
        await doc.list_items(
            [
                "Oblicz E₁ dla elektronu w studni o szerokości 0,50 nm i porównaj wynik "
                "z przypadkiem L = 1,00 nm.",
                "Wyznacz liczbę węzłów wewnętrznych funkcji ψ₄(x) i naszkicuj jej "
                "gęstość prawdopodobieństwa.",
                "Policz prawdopodobieństwo znalezienia cząstki w lewej połowie studni "
                "dla dowolnego stanu własnego.",
                "Dla superpozycji (ψ₁ + ψ₂)/√2 oblicz prawdopodobieństwa pomiaru E₁ i E₂.",
            ],
            numbered=True,
        )
        await doc.paragraph(
            "Najważniejsza rzecz do zapamiętania",
            "Toolkit Note",
        )
        await doc.paragraph(
            "Kwantowanie energii nie zostało dopisane ręcznie. Wyrasta z równania "
            "różniczkowego i warunków brzegowych. Ściany studni nie pozwalają dowolnej "
            "fali przetrwać; zostają wyłącznie fale stojące, które mieszczą całkowitą "
            "liczbę połówek długości fali w przedziale L."
        )

        await doc.mutate(
            "manage_headers_footers",
            {
                "action": "set_text",
                "story_kind": "header",
                "variant": "default",
                "section_index": 0,
                "text": "WORDTOOLKIT  •  MATEMATYKA  •  NIESKOŃCZONA STUDNIA POTENCJAŁU",
                "tracked": False,
            },
        )
        await doc.mutate(
            "manage_headers_footers",
            {
                "action": "set_text",
                "story_kind": "footer",
                "variant": "default",
                "section_index": 0,
                "text": "Strona {{PAGE}} z {{NUMPAGES}}",
                "tracked": False,
            },
        )

        inspection = (await doc.read("inspect_document"))["data"]["result"]
        equations = (await doc.read("list_equations"))["data"]["result"]
        equation_validation = (await doc.read("validate_equations"))["data"]["result"]
        accessibility = (await doc.read("check_accessibility"))["data"]["result"]
        layout = (await doc.read("check_layout_risks"))["data"]["result"]
        ooxml = (await doc.read("validate_ooxml"))["data"]["validation"]
        official = ooxml["validators"]["microsoft_openxml_sdk"]

        if len(equations) != len(doc.equation_ids):
            raise RuntimeError(
                f"Equation inventory mismatch: {len(equations)} != {len(doc.equation_ids)}"
            )
        if not equation_validation["valid"]:
            raise RuntimeError(f"Native equation validation failed: {equation_validation}")
        if accessibility["issue_count"] != 0:
            raise RuntimeError(f"Accessibility audit failed: {accessibility}")
        if not ooxml["valid"] or not official["available"] or not official["valid"]:
            raise RuntimeError(f"OOXML validation failed: {ooxml}")

        preview = await doc.mutate(
            "generate_preview",
            {
                "max_pages": 20,
                "dpi": 120,
            },
        )
        preview_data = preview["data"]
        if not preview_data["visual_audit"]["passed"]:
            raise RuntimeError(f"Automated visual audit failed: {preview_data['visual_audit']}")

        exported = await doc.mutate(
            "export_document",
            {
                "output_format": "docx",
                "file_name": output.name,
            },
        )
        source_docx = artifact_path(exported["data"]["artifact"]["download_url"])
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source_docx, output)

        preview_dir = output.with_name(f"{output.stem}_podglad")
        if preview_dir.exists():
            resolved_preview = preview_dir.resolve()
            resolved_parent = output.parent.resolve()
            if (
                resolved_preview.parent != resolved_parent
                or resolved_preview.name != f"{output.stem}_podglad"
            ):
                raise RuntimeError(
                    f"Refusing to replace unexpected preview directory: {resolved_preview}"
                )
            shutil.rmtree(resolved_preview)
        preview_dir.mkdir(parents=True, exist_ok=True)
        copied_preview: list[str] = []
        for artifact in preview_data["artifacts"]:
            source = artifact_path(artifact["download_url"])
            destination = preview_dir / source.name
            shutil.copy2(source, destination)
            copied_preview.append(str(destination))

        report = {
            "passed": True,
            "server": initialized.serverInfo.name,
            "installed_tool_count": len(tools.tools),
            "document_id": doc.document_id,
            "final_draft_version": doc.version,
            "output": str(output),
            "output_bytes": output.stat().st_size,
            "preview_files": copied_preview,
            "preview_pages": preview_data["page_count"],
            "visual_audit": preview_data["visual_audit"],
            "paragraphs": inspection["info"].get("paragraph_count"),
            "headings": len(doc.heading_ids),
            "equations": len(equations),
            "equation_validation": equation_validation,
            "accessibility": accessibility,
            "layout": layout,
            "ooxml_valid": ooxml["valid"],
            "microsoft_openxml_sdk": official,
        }
        report_path = output.with_suffix(".report.json")
        report_path.write_text(
            json.dumps(report, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )

        payload(
            await session.call_tool(
                "close_document",
                {"document_id": doc.document_id, "expected_version": doc.version},
            )
        )
        return report


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Create a polished Polish mathematics lesson as a validated DOCX."
    )
    parser.add_argument("--plugin", type=Path, default=DEFAULT_PLUGIN)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    report = asyncio.run(build_document(args.plugin.resolve(), args.output.resolve()))
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
