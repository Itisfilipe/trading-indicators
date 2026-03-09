#!/usr/bin/env python3
"""Convert NTSL PDF documentation to split Markdown files by chapter."""

import re
import subprocess
import os
import unicodedata

PDF_PATH = "/Users/filipe/Code/renko-indicators/profit-chart/ManualNTSL.pdf"
OUTPUT_DIR = "/Users/filipe/Code/renko-indicators/profit-chart/docs"

# Known chapter titles for validation (from TOC)
KNOWN_CHAPTERS = {
    1: "Introdução",
    2: "Estrutura de uma Estratégia",
    3: "Fluxo de Execução de uma Estratégia",
    4: "Variáveis, tipos de dados e constantes",
    5: "Controle de Fluxo",
    6: "Operadores",
    7: "Function (Função)",
    8: "Procedure (Procedimento)",
    9: "Initialization",
    10: "Back-Testing",
    11: "Automação de Estratégias",
    12: "Estratégias",
    13: "Block Builder",
    14: "Funções de Alarme",
    15: "Funções de Back-Testing",
    16: "Funções de Calendário",
    17: "Funções de Candlestick",
    18: "Exemplos",
    19: "Funções Gráficas",
    20: "Indicadores",
    21: "Funções de Livro",
    22: "Funções Matemáticas",
    23: "Funções de Opções",
    24: "Screening",
    25: "Funções de Usuário",
    26: "Funções Utils",
    27: "Anexos",
}


def slugify(text):
    """Convert text to ASCII slug for filenames."""
    text = unicodedata.normalize('NFKD', text).encode('ascii', 'ignore').decode('ascii')
    text = re.sub(r'[^a-z0-9]+', '-', text.lower())
    return text.strip('-')


def extract_text():
    result = subprocess.run(
        ["pdftotext", "-layout", PDF_PATH, "-"],
        capture_output=True, text=True
    )
    return result.stdout


def is_page_number(line):
    """Centered standalone number = page number."""
    stripped = line.strip()
    if re.match(r'^\d{1,3}$', stripped):
        leading = len(line) - len(line.lstrip())
        return leading > 15
    return False


def is_toc_line(line):
    """TOC lines have dot leaders like '. . . . . . . . .'"""
    return '. . . .' in line


def parse_code_line(line):
    """Parse a numbered code line. Returns (num, raw_code_with_indent) or None.
    Works on the raw line to preserve indentation."""
    # Match: optional leading space, then number, then 2+ spaces, then code
    m = re.match(r'^(\d{1,3})(\s{2,})(.*)$', line.lstrip())
    if m:
        return int(m.group(1)), m.group(2) + m.group(3)
    # Also match a line that is JUST a number (blank code line)
    m2 = re.match(r'^(\d{1,3})\s*$', line.strip())
    if m2:
        num = int(m2.group(1))
        if not is_page_number(line):
            return num, ''
    return None


def is_chapter_heading(line, last_chapter_num=0):
    """Match '14 Funções de Alarme' at line start. Must be a known chapter."""
    stripped = line.strip()
    m = re.match(r'^(\d{1,2})\s+([A-ZÁÀÂÃÉÈÊÍÏÓÔÕÖÚÇÑ].{2,})$', stripped)
    if m and '.' not in m.group(1):
        num = int(m.group(1))
        title = m.group(2)
        # Reject if title contains code-like characters
        if any(c in title for c in [':=', ';', '()', '{', '}']):
            return None
        # Must be a known chapter or at least monotonically increasing
        if num in KNOWN_CHAPTERS and num > last_chapter_num:
            return num, title
    return None


def is_section_heading(line):
    """Match '4.5 Acessando dados anteriores' or '20.112 Função Xyz'."""
    stripped = line.strip()
    m = re.match(r'^(\d{1,2}\.\d{1,3})\s+([A-ZÁÀÂÃÉÈÊÍÏÓÔÕÖÚÇÑ].{2,})$', stripped)
    if m:
        title = m.group(2)
        # Reject code-like content
        if any(c in title for c in [':=', '()', '{', '}']):
            return None
        return m.group(1), title
    return None


def collect_code_block(lines, start_idx):
    """Collect a full code block starting at start_idx. Returns (code_lines, next_idx)."""
    code_lines = []
    i = start_idx
    expected = 1

    while i < len(lines):
        line = lines[i]

        # Skip page numbers mid-code
        if is_page_number(line):
            i += 1
            continue

        parsed = parse_code_line(line)
        if parsed:
            num, code = parsed
            if num == expected:
                code_lines.append(code)
                expected = num + 1
                i += 1
                continue
            elif num == expected + 1:
                # Skipped a number (rare), add blank line
                code_lines.append('')
                code_lines.append(code)
                expected = num + 1
                i += 1
                continue
            else:
                # Number doesn't match sequence - end of block
                break
        elif line.strip() == '' or line.strip() == '\x0c':
            # Blank line - check if code continues after
            j = i + 1
            while j < len(lines) and (lines[j].strip() == '' or is_page_number(lines[j]) or lines[j].strip() == '\x0c'):
                j += 1
            if j < len(lines):
                next_parsed = parse_code_line(lines[j])
                if next_parsed and next_parsed[0] == expected:
                    # Code continues after blank lines
                    code_lines.append('')
                    i += 1
                    continue
            # Code block ended
            break
        else:
            # Non-code line - end of block
            break

    # Normalize indentation: strip common leading whitespace
    non_empty = [l for l in code_lines if l.strip()]
    if non_empty:
        min_indent = min(len(l) - len(l.lstrip()) for l in non_empty)
        code_lines = [l[min_indent:] if len(l) > min_indent else l for l in code_lines]

    return code_lines, i


def process_text(text):
    lines = text.split('\n')
    chapters = []
    current_chapter = {'num': 0, 'title': 'Capa', 'lines': []}

    # Skip cover page and TOC
    content_start = 0
    for i, line in enumerate(lines):
        # Find first actual chapter heading (not in TOC)
        if is_toc_line(line):
            continue
        ch = is_chapter_heading(line)
        if ch and ch[0] == 1 and not is_toc_line(line):
            # Make sure it's not the TOC entry "1  Introdução  1"
            # Real heading won't have trailing page number
            stripped = line.strip()
            if not re.search(r'\d+$', stripped.rstrip()) or stripped == f"{ch[0]} {ch[1]}":
                content_start = i
                break

    # Find actual content start more carefully:
    # The TOC has lines like "1     Introdução                   1"
    # The actual chapter heading is just "1 Introdução"
    # Let's find where TOC ends by looking for last '. . . .' line
    toc_end = 0
    for i, line in enumerate(lines):
        if is_toc_line(line):
            toc_end = i

    content_start = toc_end + 1
    # Skip blank lines after TOC
    while content_start < len(lines) and lines[content_start].strip() in ('', '\x0c'):
        content_start += 1

    i = content_start
    last_chapter_num = 0

    while i < len(lines):
        line = lines[i]

        # Skip form feeds
        if line.strip() == '\x0c':
            i += 1
            continue

        # Skip page numbers
        if is_page_number(line):
            i += 1
            continue

        # Check chapter heading
        ch = is_chapter_heading(line, last_chapter_num)
        if ch:
            num, title = ch
            # Save previous chapter
            if current_chapter['lines'] or current_chapter['num'] > 0:
                chapters.append(current_chapter)
            current_chapter = {
                'num': num,
                'title': title,
                'lines': [f"# {num} {title}", ""]
            }
            last_chapter_num = num
            i += 1
            continue

        # Check section heading
        sec = is_section_heading(line)
        if sec:
            num_str, title = sec
            parts = num_str.split('.')
            depth = len(parts)
            prefix = '#' * min(depth + 1, 4)
            current_chapter['lines'].append(f"{prefix} {num_str} {title}")
            current_chapter['lines'].append("")
            i += 1
            continue

        # Check code block start
        parsed = parse_code_line(line)
        if parsed and parsed[0] == 1:
            code_lines, next_i = collect_code_block(lines, i)
            if len(code_lines) >= 1:
                current_chapter['lines'].append("```pascal")
                for cl in code_lines:
                    current_chapter['lines'].append(cl)
                current_chapter['lines'].append("```")
                current_chapter['lines'].append("")
                i = next_i
                continue

        # Regular content processing
        stripped = line.strip()

        if not stripped:
            current_chapter['lines'].append("")
            i += 1
            continue

        # Bullet points
        if stripped.startswith('•'):
            text = stripped[1:].strip()
            # Check for continuation on next lines (indented text without bullet)
            j = i + 1
            while j < len(lines):
                next_s = lines[j].strip()
                if next_s and not next_s.startswith('•') and not next_s.startswith('–'):
                    leading = len(lines[j]) - len(lines[j].lstrip())
                    if leading >= 6 and not is_section_heading(lines[j]) and not is_chapter_heading(lines[j]):
                        text += ' ' + next_s
                        j += 1
                    else:
                        break
                else:
                    break
            current_chapter['lines'].append(f"- {text}")
            i = j
            continue

        if stripped.startswith('–'):
            text = stripped[1:].strip()
            j = i + 1
            while j < len(lines):
                next_s = lines[j].strip()
                if next_s and not next_s.startswith('•') and not next_s.startswith('–'):
                    leading = len(lines[j]) - len(lines[j].lstrip())
                    if leading >= 8 and not is_section_heading(lines[j]) and not is_chapter_heading(lines[j]):
                        text += ' ' + next_s
                        j += 1
                    else:
                        break
                else:
                    break
            current_chapter['lines'].append(f"  - {text}")
            i = j
            continue

        # Bold labels
        if stripped in ('Sintaxe:', 'Parâmetros:', 'Exemplo de uso:', 'Boas práticas',
                        'Observações e limitações', 'Exemplo Gráfico:',
                        'Observações:', 'Observação:', 'Retorno:'):
            if stripped == 'Exemplo Gráfico:':
                current_chapter['lines'].append("**Exemplo Gráfico:** *(imagem disponível no PDF original)*")
            else:
                current_chapter['lines'].append(f"**{stripped}**")
            current_chapter['lines'].append("")
            i += 1
            continue

        # Availability notes
        if stripped.startswith('Disponível'):
            current_chapter['lines'].append(f"> {stripped}")
            current_chapter['lines'].append("")
            i += 1
            continue

        # Function signatures (indented lines starting with "Função")
        if stripped.startswith('Função ') and ('(' in stripped or ':' in stripped):
            # Could be a multi-line signature
            sig = stripped
            j = i + 1
            while j < len(lines):
                ns = lines[j].strip()
                if ns and not ns.startswith('Função') and not ns.startswith('Parâmetros') and not is_section_heading(lines[j]):
                    leading = len(lines[j]) - len(lines[j].lstrip())
                    if leading >= 6 and '):' not in sig:
                        sig += ' ' + ns
                        j += 1
                    else:
                        break
                else:
                    break
            current_chapter['lines'].append(f"`{sig}`")
            current_chapter['lines'].append("")
            i = j
            continue

        # Parameter descriptions (indented, starting with ParamName: Type;)
        param_match = re.match(r'^(\w+):\s+(Integer|Float|Boolean|String|Serie|Ativo|Void)\b', stripped)
        if param_match:
            # Collect full parameter description (may span lines)
            desc = stripped
            j = i + 1
            while j < len(lines):
                ns = lines[j].strip()
                if ns and not ns.startswith('•') and not ns.startswith('–'):
                    leading = len(lines[j]) - len(lines[j].lstrip())
                    pnext = re.match(r'^(\w+):\s+(Integer|Float|Boolean|String|Serie|Ativo)', ns)
                    if pnext or is_section_heading(lines[j]) or is_chapter_heading(lines[j]) or ns.startswith('Exemplo') or ns.startswith('Função'):
                        break
                    if leading >= 4:
                        desc += ' ' + ns
                        j += 1
                    else:
                        break
                else:
                    break
            current_chapter['lines'].append(f"- **{desc}**" if ';' in desc else desc)
            i = j
            continue

        # Tables - detect aligned columns (heuristic: 2+ groups of text separated by 3+ spaces)
        # We'll keep tables as-is in a code block for proper alignment
        if re.search(r'\S\s{3,}\S', stripped) and not stripped.startswith('Função'):
            # Could be a table row - check context
            pass

        # Regular paragraph text - join continuation lines
        text = stripped
        j = i + 1
        while j < len(lines):
            ns = lines[j].strip()
            if not ns:
                break
            leading = len(lines[j]) - len(lines[j].lstrip())
            # Continuation of paragraph: indented text, not a heading, not bullet, not code
            if (not is_chapter_heading(lines[j], last_chapter_num) and
                not is_section_heading(lines[j]) and
                not ns.startswith('•') and
                not ns.startswith('–') and
                not ns.startswith('Função ') and
                not ns.startswith('Disponível') and
                not ns.startswith('Sintaxe:') and
                not ns.startswith('Parâmetros:') and
                not ns.startswith('Exemplo') and
                not ns.startswith('Boas práticas') and
                not ns.startswith('Observações') and
                not ns.startswith('Retorno:') and
                not is_page_number(lines[j]) and
                not parse_code_line(lines[j])):
                # Check if this looks like a continuation (same indentation level or wrapped text)
                if re.search(r'\S\s{3,}\S', ns) and re.search(r'\d{2}/\d{2}/\d{4}', ns):
                    # Table row - don't merge
                    break
                param_m = re.match(r'^(\w+):\s+(Integer|Float|Boolean|String|Serie|Ativo)', ns)
                if param_m:
                    break
                # Only merge if previous line didn't end with a period/colon suggesting end of paragraph
                text += ' ' + ns
                j += 1
            else:
                break

        current_chapter['lines'].append(text)
        i = j
        continue

    # Save last chapter
    if current_chapter['lines']:
        chapters.append(current_chapter)

    return chapters


def clean_consecutive_blanks(lines):
    """Reduce 3+ consecutive blank lines to 2."""
    result = []
    blank_count = 0
    for line in lines:
        if line.strip() == '':
            blank_count += 1
            if blank_count <= 2:
                result.append(line)
        else:
            blank_count = 0
            result.append(line)
    return result


def write_chapters(chapters):
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    index_lines = [
        "# NTSL - Documentação do Módulo Estratégias",
        "",
        "**Versão 4.2** - 20/01/2026",
        "",
        "Documentação completa da linguagem NTSL (Nelogica Trading Strategy Language) para o módulo de Estratégias da plataforma Profit Chart.",
        "",
        "## Capítulos",
        ""
    ]

    for ch in chapters:
        if ch['num'] == 0:
            continue  # Skip cover/TOC

        num = ch['num']
        title = ch['title']
        # Create filename
        slug = slugify(title)
        filename = f"{num:02d}-{slug}.md"
        filepath = os.path.join(OUTPUT_DIR, filename)

        content = clean_consecutive_blanks(ch['lines'])

        # Write chapter file
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write('\n'.join(content))
            f.write('\n')

        index_lines.append(f"- [{num}. {title}]({filename})")
        print(f"  Written: {filename} ({len(content)} lines)")

    # Write index
    index_path = os.path.join(OUTPUT_DIR, "README.md")
    with open(index_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(index_lines))
        f.write('\n')
    print(f"  Written: README.md")


def main():
    print("Extracting text from PDF...")
    text = extract_text()
    print(f"  Extracted {len(text)} chars, {text.count(chr(10))} lines")

    print("Processing text...")
    chapters = process_text(text)
    print(f"  Found {len(chapters)} chapters")

    print("Writing markdown files...")
    write_chapters(chapters)
    print("Done!")


if __name__ == '__main__':
    main()
