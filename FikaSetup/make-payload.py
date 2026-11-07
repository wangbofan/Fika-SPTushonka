#!/usr/bin/env python3
"""把构建产物打包成安装器内嵌的 payload.zip。

用法:
    python make-payload.py --game "E://TKF" --core <Fika.Core.dll> --headless <Fika.Headless.dll> \
        --server <FikaServer 输出目录> --manager <FikaHeadlessManager.exe>

payload 结构（与安装器约定一致）:
    BepInEx/plugins/Fika/Fika.Core.dll
    BepInEx/plugins/Fika/Fika.Headless.dll
    FikaHeadlessManager.exe
    SPT_Runtime/user/mods/FikaServer/...   (FikaServer.dll / FikaShared.dll / assets)
"""
import argparse, os, sys, zipfile

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--game', required=True, help='已装好 Fika 的游戏根目录（提供 FikaServer 的 assets）')
    ap.add_argument('--core', required=True)
    ap.add_argument('--headless', required=True)
    ap.add_argument('--server', required=True, help='FikaServer 的构建输出目录')
    ap.add_argument('--manager', required=True)
    ap.add_argument('--out', default='payload.zip')
    a = ap.parse_args()

    server_assets = os.path.join(a.game, r'SPT_Runtime\user\mods\FikaServer\assets')
    entries = []

    def add(z, path, arc):
        z.write(path, arc)
        entries.append(arc)

    with zipfile.ZipFile(a.out, 'w', zipfile.ZIP_DEFLATED) as z:
        add(z, a.core, 'BepInEx/plugins/Fika/Fika.Core.dll')
        add(z, a.headless, 'BepInEx/plugins/Fika/Fika.Headless.dll')
        add(z, a.manager, 'FikaHeadlessManager.exe')

        for name in ('FikaServer.dll', 'FikaShared.dll', 'FikaServer.deps.json',
                     'FikaServer.staticwebassets.endpoints.json', 'FikaServer.staticwebassets.runtime.json'):
            p = os.path.join(a.server, name)
            if os.path.exists(p):
                add(z, p, 'SPT_Runtime/user/mods/FikaServer/' + name)

        if os.path.isdir(server_assets):
            for root, _, files in os.walk(server_assets):
                for f in files:
                    p = os.path.join(root, f)
                    rel = os.path.relpath(p, server_assets).replace('\\', '/')
                    add(z, p, 'SPT_Runtime/user/mods/FikaServer/assets/' + rel)
        else:
            print(f'[warn] 未找到 {server_assets}，服务端本地化资源将缺失', file=sys.stderr)

    print(f'{a.out}: {len(entries)} 个条目, {os.path.getsize(a.out)//1024} KB')

if __name__ == '__main__':
    main()
