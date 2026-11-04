# 官方插件 AtomGit 镜像

目标仓库：https://gitcode.com/Star-Pie/StarPie-Official-Plugins 。代码已由用户导入；这不代表发行附件已同步。2026-11-04 的匿名 API 检查返回空 Release 列表。以下说明介绍代码镜像与发行附件同步的配置和首次验收；实际运行前需将工作流审核合并到 GitHub 默认分支。

## 一、保持代码和标签同步

在 AtomGit 项目的设置中配置 GitHub URL 的 Pull 仓库镜像，源地址为 `https://github.com/Star-Pie/StarPie-Official-Plugins.git`。首次运行附件同步前，先同步代码及全部需要的 tag。

附件工作流核对两边 tag 指向的 commit；标签未同步或 commit 不一致时停止，不创建或移动标签。

官方说明：[仓库镜像](https://docs.atomgit.com/docs/help/home/org_project/project_manage/project_settings/repository_mirroring/)、[仓库导入](https://docs.atomgit.com/en/docs/help/home/org_project/project_manage/getting_started/project_creation_and_import/)。Git 导入不包含 Release 附件。

## 二、在 GitHub 保存凭据

在 **Star-Pie/StarPie-Official-Plugins** 的 Settings → Secrets and variables → Actions 配置：

| 类型 | 名称 | 值 |
| --- | --- | --- |
| Repository secret | `ATOMGIT_TOKEN` | 对目标镜像仓库有发行版写权限的 AtomGit 访问令牌 |
| Repository variable，可省略 | `ATOMGIT_REPOSITORY` | `Star-Pie/StarPie-Official-Plugins` |
| Repository variable，首次补齐后再开 | `ATOMGIT_MIRROR_ENABLED` | `true` |

令牌只保存在 GitHub Secret；不写入仓库、不发到聊天。组织级 Secret 需要给此插件仓库访问权限。自动同步变量未启用时，不影响原 GitHub 发行流程。

## 三、首次补齐历史附件

本地改动审核并获授权提交到默认分支后，在 GitHub Actions 选择 **Sync Release to AtomGit** → **Run workflow**。`tag` 填写完整、已在 GitHub 发布的标签；上传客户端保留 `curl`。每次等待成功后再同步下一项。

不能只同步最新 tag：最新 catalog 会引用历史版本的包。对 2026-11-04 读取的 `modules-2026.10.15.1/module-catalog.json`，需按以下顺序补齐：

1. `modules-2026.09.25.2`
2. `modules-2026.10.01.1`
3. `modules-2026.10.10.1`
4. `modules-2026.10.11.1`
5. `modules-2026.10.12.1`
6. `modules-2026.10.13.1`
7. `modules-2026.10.15.1`

此列表绑定上述目录快照。若 GitHub 已有更新目录，应先读取其 `modules[].versions[].releaseTag`，去重后同步全部引用标签，最后同步新目录所在标签。

工作流复制原始附件，下载回读校验 SHA-256；同名同内容复用，同名不同内容停止，不覆盖或删除。`module-catalog.json` 最后上传；同步中断可用同一 tag 重试。镜像发行说明末尾的隐藏注释保留 GitHub 发布时间，请勿删除，它用于避免历史补同步改变目录顺序。客户端有界检查最近100个镜像发行元数据，并从按源发布时间排序后的8个目录候选中选择。

未带合法发布时间标记的手工发行版、旧同步器产物或半成品不参与目录发现；请用本同步流程重试同一 tag，核验后补齐标记。这样旧半成品在最后一个包上传后、最终备注更新前中断，也不会覆盖缓存。

## 四、验收后启用自动同步

先确认镜像中每个引用标签的包和 catalog 齐全，并从隔离 StarPie 副本选择 AtomGit 刷新目录，下载核心插件及一个普通插件，核对安装、失败提示和已有禁用状态。不可将离线 HTTP 夹具通过视为真实镜像下载通过。

验收通过后将 `ATOMGIT_MIRROR_ENABLED` 设为 `true`。以后 **Release Official Modules** 非 dry-run 发布成功后调用相同同步流程；失败可手动按原 tag 重试。GitHub 发布与镜像同步是独立结果，镜像失败不会删除 GitHub 发行物。

可手动选择原有 GitHub直连或代理源。选择 AtomGit 后不会静默回退到 GitHub；未同步附件会明确失败，不修改已有插件。
