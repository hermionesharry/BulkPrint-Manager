"""Creates the sample documents used by the automated self-test."""
import sys, os, io
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfgen import canvas
from pypdf import PdfReader, PdfWriter
from PIL import Image, ImageDraw, ImageFont
import docx
from docx.shared import Pt
import openpyxl
from pptx import Presentation
from pptx.util import Inches, Pt as PPt

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
F = "/usr/share/fonts/opentype/inter/Inter-Regular.otf"

# Multi-page PDF with one landscape page (tests automatic orientation and page ranges).
c = canvas.Canvas(f"{OUT}/sample-report.pdf", pagesize=A4)
for i in range(1, 4):
    size = landscape(A4) if i == 2 else A4
    c.setPageSize(size)
    w, h = size
    c.setStrokeColorRGB(0.12, 0.37, 0.84); c.setLineWidth(3); c.rect(36, 36, w - 72, h - 72)
    c.setFont("Helvetica-Bold", 28); c.drawString(72, h - 110, f"Sample report - page {i} of 3")
    c.setFont("Helvetica", 13)
    c.drawString(72, h - 140, "Landscape page (automatic orientation test)" if i == 2 else "Portrait page")
    for j in range(12):
        c.drawString(72, h - 190 - j * 22, f"Line {j + 1}: Food technology lab result batch #{i}{j:02d} - moisture 12.{j}%")
    c.showPage()
c.save()

# Password-protected PDF (password: test1234).
w = PdfWriter()
for p in PdfReader(f"{OUT}/sample-report.pdf").pages: w.add_page(p)
w.encrypt(user_password="test1234", owner_password="owner-test1234", algorithm="AES-128")
with open(f"{OUT}/sample-protected.pdf", "wb") as fh: w.write(fh)

# Damaged PDF (must be reported as failed, never printed).
with open(f"{OUT}/sample-corrupt.pdf", "wb") as fh: fh.write(b"%PDF-1.7\nthis is not a real pdf file\n" + os.urandom(300))

# Word document.
d = docx.Document()
d.add_heading("Sample letter", 0)
d.add_paragraph("Dear student,")
for i in range(8):
    d.add_paragraph(f"Paragraph {i + 1}. This Word document checks that BulkPrint Manager converts and prints DOCX files offline, "
                    "without Microsoft Word installed. Tables, headings and bold text should look right.")
t = d.add_table(rows=4, cols=3); t.style = "Table Grid"
for r in range(4):
    for col in range(3): t.cell(r, col).text = f"R{r + 1}C{col + 1}"
d.save(f"{OUT}/sample-letter.docx")

# RTF.
rtf = r"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\f0\fs28\b Sample RTF memo\b0\par\fs22 This RTF file is printed through the built-in Office engine.\par Second line with \i italic\i0  and \b bold\b0  text.\par}"
open(f"{OUT}/sample-memo.rtf", "w").write(rtf)

# Excel workbook with two sheets.
wb = openpyxl.Workbook(); ws = wb.active; ws.title = "Budget"
ws.append(["Item", "Quantity", "Unit price", "Total"])
for i in range(1, 41): ws.append([f"Ingredient {i}", i, 12.5 + i, f"=B{i + 1}*C{i + 1}"])
ws2 = wb.create_sheet("Summary"); ws2.append(["Grand total", "=SUM(Budget!D2:D41)"])
wb.save(f"{OUT}/sample-budget.xlsx")

# PowerPoint.
prs = Presentation()
for i in range(1, 4):
    s = prs.slides.add_slide(prs.slide_layouts[1])
    s.shapes.title.text = f"Sample slide {i}"
    s.placeholders[1].text = "Printed without PowerPoint installed\nOffline conversion check"
prs.save(f"{OUT}/sample-slides.pptx")

# CSV with quoted values.
open(f"{OUT}/sample-data.csv", "w", encoding="utf-8").write(
    "Sample ID,Product,Moisture %,Notes\n" + "\n".join(f'S{i:03d},"Biscuit, batch {i}",{11 + i / 10:.1f},OK' for i in range(1, 121)) + "\n")

# Long text file (spans several pages).
open(f"{OUT}/sample-notes.txt", "w", encoding="utf-8").write(
    "BulkPrint Manager sample text file\n\n" + "\n".join(f"{i:03d}\tThis is line {i} of the plain text sample. " * 1 for i in range(1, 181)))

# Images.
def picture(size, label, color):
    im = Image.new("RGB", size, color)
    dr = ImageDraw.Draw(im)
    dr.rectangle([10, 10, size[0] - 10, size[1] - 10], outline=(20, 33, 61), width=8)
    dr.text((40, 40), label, fill=(20, 33, 61), font=ImageFont.truetype(F, max(24, size[0] // 18)))
    return im

# Photo stored sideways with EXIF orientation 6 (should print upright).
photo = picture((1600, 1200), "PHOTO - should print upright (EXIF rotated)", (250, 235, 200)).rotate(90, expand=True)  # pixels stored sideways
exif = Image.Exif(); exif[0x0112] = 6
photo.save(f"{OUT}/sample-photo.jpg", quality=88, exif=exif.tobytes(), dpi=(150, 150))
picture((1200, 800), "PNG chart sample", (220, 235, 255)).save(f"{OUT}/sample-chart.png", dpi=(150, 150))
picture((800, 1100), "BMP scan sample", (240, 240, 240)).save(f"{OUT}/sample-scan.bmp")
pages = [picture((1240, 1754), f"TIFF fax page {i}", (255, 255, 255)) for i in (1, 2)]
pages[0].save(f"{OUT}/sample-fax.tiff", save_all=True, append_images=pages[1:], dpi=(150, 150), compression="tiff_lzw")
print("samples written:", sorted(os.listdir(OUT)))
