import os
import sys

def convert_md_to_html(md_path, html_path):
    with open(md_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # Basic HTML wrapping with CSS for printing to PDF
    html_content = f"""<!DOCTYPE html>
<html lang="vi">
<head>
    <meta charset="UTF-8">
    <title>GIÁO TRÌNH LẬP TRÌNH OPENMEDIASDK</title>
    <style>
        @page {{
            size: A4;
            margin: 20mm 15mm 20mm 15mm;
            @bottom-center {{
                content: counter(page);
            }}
        }}
        body {{
            font-family: 'Segoe UI', Arial, sans-serif;
            line-height: 1.6;
            color: #1a1a1a;
            max-width: 900px;
            margin: 0 auto;
            padding: 20px;
        }}
        h1 {{
            color: #0d47a1;
            text-align: center;
            border-bottom: 3px solid #0d47a1;
            padding-bottom: 10px;
            font-size: 26px;
        }}
        h2 {{
            color: #1565c0;
            border-bottom: 2px solid #e0e0e0;
            padding-bottom: 5px;
            margin-top: 30px;
            font-size: 20px;
            page-break-after: avoid;
        }}
        h3 {{
            color: #0277bd;
            font-size: 16px;
            margin-top: 20px;
            page-break-after: avoid;
        }}
        code {{
            font-family: 'Consolas', 'Courier New', monospace;
            background-color: #f4f5f7;
            padding: 2px 5px;
            border-radius: 4px;
            font-size: 13px;
            color: #d32f2f;
        }}
        pre {{
            background-color: #1e1e1e;
            color: #d4d4d4;
            padding: 15px;
            border-radius: 6px;
            overflow-x: auto;
            font-family: 'Consolas', monospace;
            font-size: 12px;
            line-height: 1.4;
            page-break-inside: avoid;
        }}
        pre code {{
            background-color: transparent;
            color: inherit;
            padding: 0;
        }}
        blockquote {{
            border-left: 4px solid #0288d1;
            background-color: #e1f5fe;
            margin: 15px 0;
            padding: 10px 15px;
            border-radius: 0 4px 4px 0;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
            page-break-inside: avoid;
        }}
        th, td {{
            border: 1px solid #e0e0e0;
            padding: 10px;
            text-align: left;
        }}
        th {{
            background-color: #f5f5f5;
            color: #333;
            font-weight: bold;
        }}
        .footer {{
            text-align: center;
            margin-top: 50px;
            font-size: 12px;
            color: #666;
            border-top: 1px solid #ddd;
            padding-top: 10px;
        }}
    </style>
</head>
<body>
"""

    # Convert simple markdown tags to HTML elements
    lines = content.split('\n')
    in_code = False
    code_lang = ""

    for line in lines:
        if line.startswith('```'):
            if not in_code:
                in_code = True
                code_lang = line.replace('```', '').strip()
                html_content += f'<pre><code class="{code_lang}">\n'
            else:
                in_code = False
                html_content += '</code></pre>\n'
            continue

        if in_code:
            escaped = line.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
            html_content += escaped + '\n'
            continue

        if line.startswith('# '):
            html_content += f'<h1>{line[2:].strip()}</h1>\n'
        elif line.startswith('## '):
            html_content += f'<h2>{line[3:].strip()}</h2>\n'
        elif line.startswith('### '):
            html_content += f'<h3>{line[4:].strip()}</h3>\n'
        elif line.startswith('#### '):
            html_content += f'<h4>{line[5:].strip()}</h4>\n'
        elif line.startswith('> '):
            html_content += f'<blockquote>{line[2:].strip()}</blockquote>\n'
        elif line.strip() == '---':
            html_content += '<hr/>\n'
        elif line.strip() == '':
            html_content += '<p></p>\n'
        else:
            # Format bold & code inline
            formatted = line
            # Simple replacements
            html_content += f'<p>{formatted}</p>\n'

    html_content += """
    <div class="footer">
        Giáo trình Lập trình OpenMediaSDK &copy; 2026 - Biên soạn bởi AI Antigravity Architect
    </div>
</body>
</html>
"""

    with open(html_path, 'w', encoding='utf-8') as f:
        f.write(html_content)
    print(f"Generated HTML for PDF at: {html_path}")

if __name__ == '__main__':
    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    md_file = os.path.join(base_dir, 'docs', 'GiaoTrinh_OpenMediaSDK.md')
    html_file = os.path.join(base_dir, 'docs', 'GiaoTrinh_OpenMediaSDK.html')
    convert_md_to_html(md_file, html_file)
