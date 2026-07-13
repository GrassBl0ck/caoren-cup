from __future__ import annotations

import argparse
import html
import os
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Literal

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Flowable,
    Frame,
    KeepTogether,
    LongTable,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    TableStyle,
)
from reportlab.platypus.tableofcontents import TableOfContents


BlockKind = Literal['heading', 'paragraph', 'bullet', 'number', 'quote', 'table']


@dataclass(frozen=True)
class MarkdownBlock:
    kind: BlockKind
    text: str = ''
    level: int = 0
    rows: tuple[tuple[str, ...], ...] = ()


def _is_table_separator(line: str) -> bool:
    cells = [cell.strip() for cell in line.strip().strip('|').split('|')]
    return bool(cells) and all(re.fullmatch(r':?-{3,}:?', cell) for cell in cells)


def _is_block_start(line: str) -> bool:
    stripped = line.strip()
    return bool(
        re.match(r'^#{1,4}\s+', stripped)
        or re.match(r'^[-*]\s+', stripped)
        or re.match(r'^\d+\.\s+', stripped)
        or stripped.startswith('> ')
        or (stripped.startswith('|') and stripped.endswith('|'))
    )


def parse_markdown(source: str) -> list[MarkdownBlock]:
    lines = source.replace('\r\n', '\n').replace('\r', '\n').split('\n')
    blocks: list[MarkdownBlock] = []
    index = 0
    while index < len(lines):
        line = lines[index].strip()
        if not line:
            index += 1
            continue

        heading = re.match(r'^(#{1,4})\s+(.+)$', line)
        if heading:
            blocks.append(MarkdownBlock('heading', heading.group(2).strip(), len(heading.group(1))))
            index += 1
            continue

        if line.startswith('|') and line.endswith('|'):
            table_lines: list[str] = []
            while index < len(lines):
                candidate = lines[index].strip()
                if not (candidate.startswith('|') and candidate.endswith('|')):
                    break
                table_lines.append(candidate)
                index += 1
            rows: list[tuple[str, ...]] = []
            for table_line in table_lines:
                if _is_table_separator(table_line):
                    continue
                rows.append(tuple(cell.strip() for cell in table_line.strip('|').split('|')))
            if rows:
                blocks.append(MarkdownBlock('table', rows=tuple(rows)))
            continue

        if line.startswith('> '):
            quote_lines: list[str] = []
            while index < len(lines) and lines[index].strip().startswith('> '):
                quote_lines.append(lines[index].strip()[2:].strip())
                index += 1
            blocks.append(MarkdownBlock('quote', ' '.join(quote_lines)))
            continue

        bullet = re.match(r'^[-*]\s+(.+)$', line)
        if bullet:
            blocks.append(MarkdownBlock('bullet', bullet.group(1).strip()))
            index += 1
            continue

        number = re.match(r'^(\d+)\.\s+(.+)$', line)
        if number:
            blocks.append(MarkdownBlock('number', f'{number.group(1)}. {number.group(2).strip()}'))
            index += 1
            continue

        paragraph_lines = [line]
        index += 1
        while index < len(lines):
            candidate = lines[index].strip()
            if not candidate or _is_block_start(candidate):
                break
            paragraph_lines.append(candidate)
            index += 1
        blocks.append(MarkdownBlock('paragraph', ' '.join(paragraph_lines)))

    return blocks


def resolve_cjk_font(explicit_path: str | None) -> str:
    candidates = [
        explicit_path,
        os.environ.get('CAOREN_RULES_FONT'),
        r'C:\Windows\Fonts\simhei.ttf',
        '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',
        '/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc',
    ]
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return str(Path(candidate).resolve())
    raise FileNotFoundError(
        '未找到中文字体。请使用 --font C:\\Windows\\Fonts\\simhei.ttf '
        '或设置 CAOREN_RULES_FONT。'
    )


def build_styles(font_name: str) -> dict[str, ParagraphStyle]:
    sample = getSampleStyleSheet()
    return {
        'title': ParagraphStyle(
            'RulesTitle', parent=sample['Title'], fontName=font_name, fontSize=26,
            leading=34, textColor=colors.HexColor('#142033'), alignment=TA_CENTER,
            spaceAfter=12,
        ),
        'subtitle': ParagraphStyle(
            'RulesSubtitle', parent=sample['Normal'], fontName=font_name, fontSize=11,
            leading=18, textColor=colors.HexColor('#64748b'), alignment=TA_CENTER,
        ),
        'toc_title': ParagraphStyle(
            'RulesTocTitle', parent=sample['Heading1'], fontName=font_name, fontSize=20,
            leading=28, textColor=colors.HexColor('#142033'), spaceAfter=16,
        ),
        'heading1': ParagraphStyle(
            'RulesHeading1', parent=sample['Heading1'], fontName=font_name, fontSize=18,
            leading=25, textColor=colors.HexColor('#1d4ed8'), spaceBefore=18,
            spaceAfter=9, keepWithNext=True,
        ),
        'heading2': ParagraphStyle(
            'RulesHeading2', parent=sample['Heading2'], fontName=font_name, fontSize=14,
            leading=21, textColor=colors.HexColor('#142033'), spaceBefore=14,
            spaceAfter=7, keepWithNext=True,
        ),
        'heading3': ParagraphStyle(
            'RulesHeading3', parent=sample['Heading3'], fontName=font_name, fontSize=11.5,
            leading=18, textColor=colors.HexColor('#334155'), spaceBefore=10,
            spaceAfter=5, keepWithNext=True,
        ),
        'heading4': ParagraphStyle(
            'RulesHeading4', parent=sample['Heading4'], fontName=font_name, fontSize=10,
            leading=16, textColor=colors.HexColor('#334155'), spaceBefore=8,
            spaceAfter=4, keepWithNext=True,
        ),
        'body': ParagraphStyle(
            'RulesBody', parent=sample['BodyText'], fontName=font_name, fontSize=9.5,
            leading=16, textColor=colors.HexColor('#263548'), spaceAfter=7,
            wordWrap='CJK',
        ),
        'list': ParagraphStyle(
            'RulesList', parent=sample['BodyText'], fontName=font_name, fontSize=9.2,
            leading=15, textColor=colors.HexColor('#263548'), leftIndent=13,
            firstLineIndent=-10, spaceAfter=4, wordWrap='CJK',
        ),
        'quote': ParagraphStyle(
            'RulesQuote', parent=sample['BodyText'], fontName=font_name, fontSize=9.2,
            leading=15, textColor=colors.HexColor('#205b35'), leftIndent=12,
            rightIndent=8, borderColor=colors.HexColor('#16a34a'), borderWidth=1.5,
            borderPadding=(7, 9, 7, 9), backColor=colors.HexColor('#eefaf1'),
            spaceBefore=4, spaceAfter=9, wordWrap='CJK',
        ),
        'table': ParagraphStyle(
            'RulesTableText', parent=sample['BodyText'], fontName=font_name, fontSize=8,
            leading=12, textColor=colors.HexColor('#263548'), wordWrap='CJK',
        ),
        'table_header': ParagraphStyle(
            'RulesTableHeader', parent=sample['BodyText'], fontName=font_name, fontSize=8.2,
            leading=12, textColor=colors.white, wordWrap='CJK',
        ),
        'footer': ParagraphStyle(
            'RulesFooter', parent=sample['Normal'], fontName=font_name, fontSize=8,
            textColor=colors.HexColor('#64748b'),
        ),
    }


def _inline_markup(value: str) -> str:
    escaped = html.escape(value, quote=False)
    escaped_angle_url = re.compile(r'&lt;(https?://[^\s<>]+)&gt;')
    escaped = escaped_angle_url.sub(r'<font color="#1d4ed8">\1</font>', escaped)
    escaped = re.sub(r'`([^`]+)`', r'<font color="#1d4ed8">\1</font>', escaped)
    escaped = re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', escaped)
    return escaped


class RulesDocTemplate(BaseDocTemplate):
    def __init__(self, filename: str, styles: dict[str, ParagraphStyle]):
        super().__init__(
            filename,
            pagesize=A4,
            leftMargin=18 * mm,
            rightMargin=18 * mm,
            topMargin=18 * mm,
            bottomMargin=17 * mm,
            title='草人杯完整规则',
            author='GrassBl0ck',
            subject='草人杯 CS2 自定义娱乐赛完整规则',
        )
        self.styles = styles
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id='rules-frame')
        self.addPageTemplates(PageTemplate(id='rules-pages', frames=[frame], onPage=self._draw_page))

    def _draw_page(self, canvas, doc) -> None:
        canvas.saveState()
        canvas.setTitle('草人杯完整规则')
        canvas.setAuthor('GrassBl0ck')
        canvas.setFont('CaorenRulesCJK', 8)
        canvas.setFillColor(colors.HexColor('#64748b'))
        canvas.drawString(self.leftMargin, 9 * mm, '草人杯完整规则')
        canvas.drawRightString(A4[0] - self.rightMargin, 9 * mm, f'第 {doc.page} 页')
        canvas.setStrokeColor(colors.HexColor('#dbe3ec'))
        canvas.line(self.leftMargin, 13 * mm, A4[0] - self.rightMargin, 13 * mm)
        canvas.restoreState()

    def afterFlowable(self, flowable: Flowable) -> None:
        if not isinstance(flowable, Paragraph):
            return
        style_name = flowable.style.name
        level_by_style = {'RulesHeading1': 0, 'RulesHeading2': 1, 'RulesHeading3': 2}
        if style_name not in level_by_style:
            return
        level = level_by_style[style_name]
        text = flowable.getPlainText()
        key = getattr(flowable, '_bookmarkName', None)
        if key:
            self.canv.bookmarkPage(key)
        self.notify('TOCEntry', (level, text, self.page, key))


def _make_heading(text: str, level: int, styles: dict[str, ParagraphStyle], anchor: str) -> Paragraph:
    paragraph = Paragraph(_inline_markup(text), styles[f'heading{level}'])
    paragraph._bookmarkName = anchor
    paragraph._bookmark = anchor
    return paragraph


def build_story(
    blocks: list[MarkdownBlock],
    styles: dict[str, ParagraphStyle],
    page_width: float,
) -> list[Flowable]:
    story: list[Flowable] = []
    title = blocks[0].text if blocks and blocks[0].kind == 'heading' else '草人杯完整规则'
    start_index = 1 if blocks and blocks[0].kind == 'heading' else 0
    date_line = ''
    if start_index < len(blocks) and blocks[start_index].kind == 'paragraph' and blocks[start_index].text.startswith('更新日期：'):
        date_line = blocks[start_index].text
        start_index += 1

    story.extend([
        Spacer(1, 55 * mm),
        Paragraph(_inline_markup(title), styles['title']),
        Paragraph(_inline_markup(date_line), styles['subtitle']),
        Spacer(1, 10 * mm),
        Paragraph('CS2 自定义娱乐赛规则手册', styles['subtitle']),
        PageBreak(),
        Paragraph('目录', styles['toc_title']),
    ])

    toc = TableOfContents()
    toc.levelStyles = [
        ParagraphStyle('TOC1', fontName='CaorenRulesCJK', fontSize=10, leading=17, leftIndent=0, textColor=colors.HexColor('#142033')),
        ParagraphStyle('TOC2', fontName='CaorenRulesCJK', fontSize=9, leading=15, leftIndent=14, textColor=colors.HexColor('#475569')),
        ParagraphStyle('TOC3', fontName='CaorenRulesCJK', fontSize=8.5, leading=14, leftIndent=28, textColor=colors.HexColor('#64748b')),
    ]
    story.extend([toc, PageBreak()])

    heading_index = 0
    for block in blocks[start_index:]:
        if block.kind == 'heading':
            heading_index += 1
            story.append(_make_heading(block.text, min(block.level, 4), styles, f'heading-{heading_index}'))
        elif block.kind == 'paragraph':
            story.append(Paragraph(_inline_markup(block.text), styles['body']))
        elif block.kind == 'bullet':
            story.append(Paragraph(f'• {_inline_markup(block.text)}', styles['list']))
        elif block.kind == 'number':
            story.append(Paragraph(_inline_markup(block.text), styles['list']))
        elif block.kind == 'quote':
            story.append(Paragraph(_inline_markup(block.text), styles['quote']))
        elif block.kind == 'table' and block.rows:
            column_count = max(len(row) for row in block.rows)
            normalized = [row + ('',) * (column_count - len(row)) for row in block.rows]
            table_data = []
            for row_index, row in enumerate(normalized):
                style = styles['table_header'] if row_index == 0 else styles['table']
                table_data.append([Paragraph(_inline_markup(cell), style) for cell in row])
            if column_count == 2:
                widths = [page_width * 0.29, page_width * 0.71]
            else:
                widths = [page_width / column_count] * column_count
            table = LongTable(table_data, colWidths=widths, repeatRows=1, splitByRow=1)
            table.setStyle(TableStyle([
                ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#2563eb')),
                ('GRID', (0, 0), (-1, -1), 0.45, colors.HexColor('#cbd5e1')),
                ('VALIGN', (0, 0), (-1, -1), 'TOP'),
                ('LEFTPADDING', (0, 0), (-1, -1), 6),
                ('RIGHTPADDING', (0, 0), (-1, -1), 6),
                ('TOPPADDING', (0, 0), (-1, -1), 5),
                ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
                ('ROWBACKGROUNDS', (0, 1), (-1, -1), [colors.white, colors.HexColor('#f8fafc')]),
            ]))
            story.append(KeepTogether([Spacer(1, 2 * mm), table, Spacer(1, 3 * mm)]))
    return story


def build_pdf(source_path: Path, output_path: Path, font_path: str | None) -> None:
    source = source_path.read_text(encoding='utf-8')
    blocks = parse_markdown(source)
    if not blocks:
        raise ValueError('规则 Markdown 为空')
    resolved_font = resolve_cjk_font(font_path)
    pdfmetrics.registerFont(TTFont('CaorenRulesCJK', resolved_font))
    styles = build_styles('CaorenRulesCJK')
    output_path.parent.mkdir(parents=True, exist_ok=True)
    document = RulesDocTemplate(str(output_path), styles)
    document.multiBuild(build_story(blocks, styles, document.width))


def main() -> None:
    parser = argparse.ArgumentParser(description='从 Markdown 生成草人杯完整规则 PDF')
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--font')
    args = parser.parse_args()
    build_pdf(args.source.resolve(), args.output.resolve(), args.font)
    print(f'Created PDF: {args.output.resolve()}')


if __name__ == '__main__':
    main()
