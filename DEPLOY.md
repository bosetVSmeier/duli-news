# IndieRadar 部署指南（Phase 3）

本文说明如何让 IndieRadar 真正跑在公网上，每天自动生成日报，前端随时能看。
按「本地验证 → 云数据库 → 部署 → 定时」的顺序，每一步都可单独验证。

---

## 一、三种运行模式（重要）

同一个程序，靠环境变量切换三种模式，业务代码完全不用改：

| 模式 | 配置 | 数据库 | AI | 用途 |
|---|---|---|---|---|
| 本地演示 | 什么都不填 | 内存 | Mock | 零依赖，启动即有假数据，看效果用 |
| 本地+真实库 | `DB_MODE=postgres` + 连接串 | 本地 Docker Postgres | Mock | 验证数据落库、持久化 |
| 生产 | 全部环境变量 | Neon 云 Postgres | 真实模型 | 公网部署 |

程序启动时读环境变量：**不填=内存+Mock，填了才切真实**。这样你随时能退回到零依赖模式。

---

## 二、本地跑起来（三种）

### 2.1 纯演示（最快，零依赖）
```bash
cd IndieRadar
dotnet run --project src/IndieRadar.Api
# 打开 http://localhost:5195
# POST /api/digest/run 生成日报
```

### 2.2 本地 + Postgres（验证数据落库）
```bash
# 1) 起本地 Postgres（数据持久化到 docker volume）
docker compose up -d

# 2) 带 DB_MODE 启动 API（自动建表+灌种子源）
DB_MODE=postgres \
CONNECTION_STRING="Host=localhost;Port=5432;Database=indieradar;Username=indieradar;Password=indieradar_dev_pwd" \
dotnet run --project src/IndieRadar.Api

# 3) 验证数据真的进库了
docker exec indieradar-pg psql -U indieradar -d indieradar -c "SELECT count(*) FROM articles;"
```
重启 API 后数据仍在——这就是 Postgres 相比内存的价值。

### 2.3 本地 + 真实 AI（需密钥）
在 2.2 基础上加：
```bash
LLM_MODE=real \
LLM_PRIMARY_API_KEY="你的TokenHub密钥" \
LLM_FALLBACK_API_KEY="你的智谱密钥" \
DB_MODE=postgres CONNECTION_STRING="..." \
dotnet run --project src/IndieRadar.Api
```
此时 AI 加工会真实调用 Hy4 flash，失败自动降级到免费的 GLM-4-Flash。

---

## 三、生产部署（Render + Neon，免费额度）

### 3.1 建 Neon 免费 Postgres
1. 打开 https://neon.tech ，用 GitHub 账号注册（免费 1GB，永久）。
2. 创建 Project，区域选离你近的（如 Singapore）。
3. 在控制台复制 **Connection String**（形如 `postgresql://user:pass@host/db?sslmode=require`）。
4. 不用手动建表——程序启动会自动建表+灌种子源。

> Neon 免费层闲置会自动休眠，首次访问有 1-2 秒唤醒延迟，自用可接受。

### 3.2 部署 API 到 Render
1. 把 `IndieRadar` 目录推到你的 GitHub 仓库。
2. 打开 https://render.com ，用 GitHub 账号登录。
3. New → Web Service → 选你的仓库。
4. 配置：
   - **Runtime**: Docker
   - **Plan**: Free
   - **Health Check Path**: `/`
5. 在 **Environment Variables** 里填（对应 `.env.example`）：
   - `DB_MODE` = `postgres`
   - `CONNECTION_STRING` = 第 3.1 步复制的连接串
   - `LLM_MODE` = `real`
   - `LLM_PRIMARY_API_KEY` = TokenHub 密钥
   - `LLM_FALLBACK_API_KEY` = 智谱密钥
   - 其余 `LLM_PRIMARY_*` / `LLM_FALLBACK_*` 用默认值即可
6. Create Web Service。首次构建约 3-5 分钟。
7. 部署完成后访问 `https://你的服务.onrender.com/`，看到 `dbMode: postgres` 即成功。

> Render 免费层 15 分钟无流量会休眠，冷启动要 30-60 秒——这就是下面定时任务的用武之地。

### 3.3 配置每日定时任务
1. 在 GitHub 仓库 → Settings → Secrets and variables → Actions，新建 secret：
   - Name: `API_BASE_URL`
   - Value: `https://你的服务.onrender.com`（不带末尾斜杠）
2. 定时任务已写在 `.github/workflows/daily-digest.yml`：
   - 每天 UTC 00:00（北京时间 08:00）自动运行
   - 先 GET 唤醒 Render，再 POST 触发生成日报
3. 可在 Actions 页面点 **Run workflow** 手动测试。

---

## 四、AI 密钥申请（动态换模型）

### 主模型 Hy4 flash（腾讯云 TokenHub）
- 申请：https://console.cloud.tencent.com/tokenhub/apikey
- Base URL：`https://tokenhub.tencentmaas.com/v1`（广州）
- Model ID：`hy4-flash`
- 价格：限时 5 折（至 2026-12-31），输入 3 元/百万 token、输出 9 元/百万，每天几十条资讯成本可忽略。
- 兼容 OpenAI 协议，本程序用标准 `/chat/completions` 调用。

### 兜底模型 GLM-4-Flash（智谱，永久免费）
- 申请：https://open.bigmodel.cn
- Base URL：`https://open.bigmodel.cn/api/paas/v4`
- Model ID：`glm-4-flash`
- 永久免费，作为主模型失败时的降级兜底。

### 想换别的模型？
只改环境变量，不用改代码。比如换成 DeepSeek：
```
LLM_PRIMARY_BASE_URL=https://api.deepseek.com/v1
LLM_PRIMARY_MODEL_ID=deepseek-chat
LLM_PRIMARY_API_KEY=你的DeepSeek密钥
```
这就是 `ILLMProvider` 抽象 + 配置三件套的价值：**换模型 = 改配置**。

---

## 五、微信小程序的域名问题（必须提前知道）

微信小程序**真机/体验版**请求后端时，要求域名：
1. 在微信公众平台后台配置为 request 合法域名；
2. 域名必须 **ICP 备案**；
3. 域名必须能解析到公网。

而 Render 免费域名（`.onrender.com`）**无法备案**——这是硬冲突。

**三条路，你选一条：**

| 方案 | 做法 | 成本 | 小程序可用性 |
|---|---|---|---|
| A. 保持免费，小程序仅开发调试 | 用 Render 免费域名，开发者工具里勾「不校验合法域名」看效果 | $0 | 真机/体验版不可用 |
| B. 国内服务器 + 备案域名 | 买域名+国内轻量服务器（阿里云/腾讯云，约 ¥100/年），备案后部署 .NET 后端 | 约 ¥100/年 | 完全可用 |
| C. 已备案域名反代 Render | 用你已有的备案域名，Nginx 反代到 Render 地址 | 域名成本 | 可用（但 Render 休眠影响体验） |

> 你之前说「备案时用工身份证备案」——那意味着要走方案 B（国内服务器），因为备案要求域名接入中国大陆服务器。
> **这会推翻「海外免费部署」的前提**。所以到真正上线前，需要先定这一条。开发阶段不受影响，先用方案 A 看效果。

---

## 六、部署检查清单

- [ ] Neon 建好，拿到 Connection String
- [ ] Render 部署成功，健康检查显示 `dbMode: postgres`
- [ ] 两个 AI 密钥填好，日报生成时日志无报错
- [ ] GitHub Actions secret `API_BASE_URL` 配好，定时任务手动测试通过
- [ ] 前端 `api/config.uts` 的 `BASE_URL` 改为 Render 地址
- [ ] （小程序上线时）域名备案 + 合法域名配置
