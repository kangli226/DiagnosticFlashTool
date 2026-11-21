# -*- coding: utf-8 -*-
import json, os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))
FONTS = r"C:\Windows\Fonts"

def F(size, bold=False):
    return ImageFont.truetype(os.path.join(FONTS, "msyhbd.ttc" if bold else "msyh.ttc"), size)

def MONO(size):
    return ImageFont.truetype(os.path.join(FONTS, "consola.ttf"), size)

def lum(h):
    h = h.lstrip('#')
    c = [int(h[i:i+2], 16)/255 for i in (0,2,4)]
    c = [x/12.92 if x <= 0.03928 else ((x+0.055)/1.055)**2.4 for x in c]
    return 0.2126*c[0] + 0.7152*c[1] + 0.0722*c[2]

def cr(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return round((hi+0.05)/(lo+0.05), 2)

A = dict(key="A", name="工业蓝 · 分层冷灰",
    chrome="#EDEFF2", chrome_border="#DDE1E5", sidebar="#EDEFF2",
    side_active="#DDE9F5", rail="#1668C4", side_text="#3F4750", side_muted="#6B737C",
    app="#F4F6F8", card="#FFFFFF", border="#D8DCE1", divider="#E9ECEF", input="#C6CBD1",
    table_head="#E9EDF1", t1="#17191B", t2="#3F4750", t3="#545C65", t4="#697078",
    primary="#1668C4", primary_hover="#1259A8", primary_soft="#E7F0FA", soft_border="#B9D3EE",
    success="#1B7F45", success_soft="#E8F5EE", warning="#96620A", warning_soft="#FBF2E1",
    error="#B5342A", error_soft="#FBEAE8", running="#0E7C86",
    log_panel="#F7F9FA", log_text="#2A3036",
    on_chrome="#3F4750", on_chrome_muted="#545C65", on_chrome_accent="#1668C4", side_active_text="#1668C4")

B = dict(key="B", name="深色框架 · 浅色内容",
    chrome="#23282E", chrome_border="#171B20", sidebar="#1C2127",
    side_active="#2E3B47", rail="#4AA3E8", side_text="#E8ECEF", side_muted="#98A2AC",
    app="#EFF2F4", card="#FFFFFF", border="#D5DADF", divider="#E7EAED", input="#C3C9CF",
    table_head="#E9ECEF", t1="#17191B", t2="#3B434B", t3="#515A63", t4="#666E77",
    primary="#1466B8", primary_hover="#0F5599", primary_soft="#E6EFF9", soft_border="#B7D2EC",
    success="#1B7F45", success_soft="#E8F5EE", warning="#96620A", warning_soft="#FBF2E1",
    error="#B5342A", error_soft="#FBEAE8", running="#0F7C87",
    log_panel="#F6F8F9", log_text="#2A3036",
    on_chrome="#E8ECEF", on_chrome_muted="#98A2AC", on_chrome_accent="#4AA3E8", side_active_text="#7FC0F2")

C = dict(key="C", name="石墨单色 · 状态专用色",
    chrome="#E9EBED", chrome_border="#D6D9DC", sidebar="#EAECEE",
    side_active="#DFE3E7", rail="#2B3238", side_text="#3D4349", side_muted="#6E747B",
    app="#F2F3F4", card="#FFFFFF", border="#D6D9DC", divider="#E6E8EA", input="#C2C6CA",
    table_head="#E8EAED", t1="#17191B", t2="#3D4349", t3="#525860", t4="#666C73",
    primary="#2B3238", primary_hover="#1F2429", primary_soft="#E6E9EC", soft_border="#C3C8CD",
    success="#1B7F45", success_soft="#E8F5EE", warning="#9A6400", warning_soft="#FBF2E1",
    error="#B5342A", error_soft="#FBEAE8", running="#1668C4",
    log_panel="#F6F7F8", log_text="#2A3036",
    on_chrome="#3D4349", on_chrome_muted="#525860", on_chrome_accent="#2B3238", side_active_text="#2B3238")

ROWS = [
    ("01", "进入扩展会话", "10 03", "", "", ""),
    ("02", "安全访问-请求种子", "27 01", "", "AES128_OneFunc", ""),
    ("03", "安全访问-发送密钥", "27 02", "", "AES128_OneFunc", ""),
    ("04", "下载驱动", "34 00", "", "", ""),
    ("05", "擦除应用区", "31 01", "FF00", "", ""),
    ("06", "请求下载", "34 00", "0x00 0x44", "", ""),
    ("07", "传输数据", "36 00", "${application.crc:u32be}", "", ""),
    ("08", "校验 CRC", "31 01", "F2,0x04,${...crc}", "CRC32_MPEG2", ""),
    ("09", "跳转应用", "11 01", "", "", ""),
    ("10", "读取故障码", "19 02", "FF", "", ""),
    ("11", "清除故障码", "14 FF", "FF FF FF", "", ""),
    ("12", "读取版本", "22 F1 89", "", "", ""),
    ("13", "写入指纹", "2E F1 84", "12 34 56 78", "", ""),
    ("14", "复位", "11 01", "01", "", ""),
]

def panel(d, box, radius=3, fill="#FFFFFF", outline=None, width=1):
    d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)

def render(p, path):
    W, H, SW = 1200, 760, 132
    img = Image.new("RGB", (W, H+SW), p["app"])
    d = ImageDraw.Draw(img)

    d.rectangle([0, 0, W, 32], fill=p["chrome"]); d.line([0, 32, W, 32], fill=p["chrome_border"])
    d.rectangle([0, 32, W, 58], fill=p["chrome"]); d.line([0, 58, W, 58], fill=p["chrome_border"])
    d.rectangle([0, 58, W, 96], fill=p["chrome"]); d.line([0, 96, W, 96], fill=p["chrome_border"])

    d.text((14, 16), "DiagnosticFlashTool", font=F(12, True), fill=p["on_chrome"], anchor="lm")
    d.text((168, 16), "ECU_A / boot_app_v1.bin", font=F(11), fill=p["on_chrome_muted"], anchor="lm")
    for i, g in enumerate(["\u00d7", "\u25a1", "\u2014"]):
        d.text((W-24-i*44, 16), g, font=F(12), fill=p["on_chrome"], anchor="mm")

    mx = 12
    for m in ["文件", "视图", "刷写", "帮助"]:
        w = d.textlength(m, font=F(12)) + 18
        d.text((mx+9, 45), m, font=F(12), fill=p["on_chrome"], anchor="lm")
        mx += w + 6

    tx = 12
    for label, col in [("刷新项目", p["on_chrome_accent"]), ("开始刷写", p["on_chrome_accent"]), ("\u25a0 停止", p["error"])]:
        w = d.textlength(label, font=F(12)) + 18
        d.text((tx+9, 77), label, font=F(12), fill=col, anchor="lm")
        tx += w + 8
    d.text((W-24, 77), "500 kbit/s    \u25cf 已连接", font=F(11), fill=p["success"], anchor="rm")

    d.rectangle([0, 96, 208, 736], fill=p["sidebar"])
    d.line([208, 96, 208, 736], fill=p["chrome_border"])
    d.text((14, 116), "项目刷写", font=F(10), fill=p["side_muted"], anchor="lm")
    y = 132
    for label, active in [("ECU_A", 0), ("  固件刷写", 0), ("  流程配置", 1), ("  项目配置", 0), ("ECU_B", 0), ("  固件刷写", 0)]:
        if active:
            d.rectangle([0, y, 208, y+34], fill=p["side_active"])
            d.rectangle([0, y+4, 2, y+30], fill=p["rail"])
            d.text((30, y+17), label, font=F(12, True), fill=p["side_active_text"], anchor="lm")
        else:
            d.text((30, y+17), label, font=F(12), fill=p["side_text"], anchor="lm")
        y += 34
    y += 10
    d.text((14, y+14), "通用 UDS", font=F(10), fill=p["side_muted"], anchor="lm")
    y += 30
    for label in ["操作日志", "开发者选项", "系统设置"]:
        d.text((30, y+17), label, font=F(12), fill=p["side_text"], anchor="lm")
        y += 34

    cx0, cx1 = 220, 1188
    panel(d, (cx0, 106, cx1, 150), 3, p["card"], p["border"])
    d.text((cx0+12, 128), "配置来源：跟随项目", font=F(12), fill=p["t2"], anchor="lm")
    d.polygon([(cx0+150, 126), (cx0+158, 126), (cx0+154, 131)], fill=p["t4"])
    d.text((cx0+182, 128), "项目：ECU_A", font=F(12), fill=p["t2"], anchor="lm")
    d.polygon([(cx0+282, 126), (cx0+290, 126), (cx0+286, 131)], fill=p["t4"])
    d.text((cx0+314, 128), "BOOT：boot_app_v1.json", font=F(12), fill=p["t2"], anchor="lm")
    d.polygon([(cx0+502, 126), (cx0+510, 126), (cx0+506, 131)], fill=p["t4"])
    bx = cx1 - 12
    for label in ["保存", "新建配置", "\u21bb 重新加载"]:
        w = d.textlength(label, font=F(12)) + 18
        bx -= w
        d.text((bx+9, 128), label, font=F(12), fill=p["primary"], anchor="lm")
        bx -= 8

    panel(d, (cx0, 158, cx1, 600), 3, p["card"], p["border"])
    d.text((cx0+12, 175), "流程节点", font=F(13, True), fill=p["t1"], anchor="lm")
    hx = cx1 - 12
    for label, col in [("校验", p["primary"]), ("\u2193 下移", p["primary"]), ("\u2191 上移", p["primary"]),
                       ("删除节点", p["error"]), ("更换模板", p["primary"]), ("\uff0b 添加节点", p["primary"])]:
        w = d.textlength(label, font=F(11)) + 14
        hx -= w
        d.text((hx+7, 175), label, font=F(11), fill=col, anchor="lm")
        hx -= 8

    d.rectangle([cx0+1, 192, cx1-1, 216], fill=p["table_head"])
    for name, x in [("ID", 240), ("名称", 296), ("SID / Sub", 536), ("扩展数据", 644), ("算法", 884), ("算法参数", 1044)]:
        d.text((x, 204), name, font=F(11, True), fill=p["t3"], anchor="lm")
    ry = 216
    for (sid, name, svc, ext, alg, par) in ROWS:
        d.line([cx0+1, ry+26, cx1-1, ry+26], fill=p["divider"])
        for dx in (0, 5):
            for dy in (-4, 0, 4):
                d.ellipse([240+dx, ry+13+dy-1, 240+dx+2, ry+13+dy+1], fill=p["t4"])
        d.text((272, ry+13), sid, font=F(11), fill=p["t3"], anchor="lm")
        d.text((296, ry+13), name, font=F(11), fill=p["t1"], anchor="lm")
        d.text((536, ry+13), svc, font=MONO(10), fill=p["t2"], anchor="lm")
        d.text((644, ry+13), ext, font=MONO(10), fill=p["t2"], anchor="lm")
        d.text((884, ry+13), alg, font=F(11), fill=p["t2"], anchor="lm")
        d.text((1044, ry+13), par, font=MONO(10), fill=p["t2"], anchor="lm")
        ry += 26

    panel(d, (cx0, 608, cx1, 710), 3, p["card"], p["border"])
    d.text((cx0+12, 626), "校验结果", font=F(13, True), fill=p["t1"], anchor="lm")
    d.rounded_rectangle([cx0+12, 642, cx1-12, 668], 2, fill=p["success_soft"])
    d.rectangle([cx0+12, 646, cx0+15, 664], fill=p["success"])
    d.text((cx0+24, 655), "\u221a 校验通过：14 个节点，1 个节点使用 CRC 计算", font=F(11), fill=p["success"], anchor="lm")
    d.rounded_rectangle([cx0+12, 672, cx1-12, 698], 2, fill=p["warning_soft"])
    d.rectangle([cx0+12, 676, cx0+15, 694], fill=p["warning"])
    d.text((cx0+24, 685), "! 节点 07 引用 ${application.crc:u32be}，需由后续 31 01 例程消费", font=F(11), fill=p["warning"], anchor="lm")

    d.rectangle([0, 736, W, 760], fill=p["chrome"])
    d.line([0, 736, W, 736], fill=p["chrome_border"])
    d.text((12, 748), "\u25cf Mock 已连接", font=F(11), fill=p["success"], anchor="lm")
    d.text((140, 748), "项目：ECU_A", font=F(11), fill=p["on_chrome_muted"], anchor="lm")
    d.text((300, 748), "就绪", font=F(11), fill=p["on_chrome_muted"], anchor="lm")
    d.text((380, 748), "最新：流程校验通过", font=F(11), fill=p["on_chrome_muted"], anchor="lm")
    d.text((W-90, 748), "128 条  查看", font=F(11), fill=p["on_chrome_muted"], anchor="lm")

    sy = 760
    d.rectangle([0, sy, W, sy+SW], fill="#FFFFFF")
    d.line([0, sy, W, sy], fill="#DDDDDD")
    sw = [("底色", p["app"]), ("卡片", p["card"]), ("框架", p["chrome"]), ("边框", p["border"]),
          ("表头", p["table_head"]), ("强调", p["primary"]), ("强调浅底", p["primary_soft"]),
          ("成功", p["success"]), ("警告", p["warning"]), ("错误", p["error"]),
          ("运行中", p["running"]), ("正文", p["t1"])]
    x = 14
    for label, hexv in sw:
        d.rounded_rectangle([x, sy+14, x+82, sy+64], 3, fill=hexv, outline="#D0D0D0")
        d.text((x, sy+76), label, font=F(10), fill="#444444", anchor="lm")
        d.text((x, sy+92), hexv.upper(), font=MONO(9), fill="#666666", anchor="lm")
        x += 96

    img.save(path)
    return path

out = []
for p in (A, B, C):
    path = os.path.join(OUT, "palette-%s.png" % p["key"].lower())
    render(p, path)
    out.append({"key": p["key"], "name": p["name"], "path": path,
        "contrast": {
            "正文/卡片": cr(p["t1"], p["card"]),
            "次要/卡片": cr(p["t2"], p["card"]),
            "弱化/底色": cr(p["t4"], p["app"]),
            "强调/卡片": cr(p["primary"], p["card"]),
            "成功/成功浅底": cr(p["success"], p["success_soft"]),
            "错误/错误浅底": cr(p["error"], p["error_soft"]),
            "卡片/底色": cr(p["card"], p["app"])}})
print(json.dumps(out, ensure_ascii=False, indent=2))
