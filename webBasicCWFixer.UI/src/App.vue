<template>
  <div class="wrap">
    <button
      class="system-test-button"
      :disabled="systemTestRunning"
      title="Sistem Testi"
      @click="runSystemTest"
    >
      ST
    </button>
    <header class="header">
      <h1>Web Basic CW Fixer</h1>
      <!-- <p class="muted">ConceptWave metadata XML – Script lint</p> -->
    </header>
    <!-- Allowlist Toggle Button -->
    <div class="row" style="justify-content: flex-end; margin-bottom: 12px">
      <button class="secondary" @click="toggleAllowlist">
        {{ showAllowlist ? "Allowlist'i Kapat" : "Allowlist Yönetimi" }}
      </button>
    </div>

    <!-- Allowlist Panel -->
    <section class="card" v-if="showAllowlist">
      <h2>Allowlist Yönetimi</h2>

      <div class="row" style="gap: 10px; margin-bottom: 12px">
        <input
          class="text"
          v-model="newRoot"
          placeholder="Yeni root ekle (örn: RegExp, Finder, tt_Common, eval...)"
          @keydown.enter="addRoot"
        />
        <button
          class="primary"
          :disabled="!newRoot.trim() || allowlistBusy"
          @click="addRoot"
        >
          Ekle
        </button>

        <button
          class="secondary"
          :disabled="allowlistBusy"
          @click="reloadAllowlist"
        >
          Yenile
        </button>

        <button
          class="secondary"
          :disabled="allowlistBusy || !allowDirty"
          @click="saveAllowlist"
        >
          Kaydet (PUT)
        </button>
      </div>

      <div class="muted" v-if="allowlistBusy">İşleniyor...</div>
      <div class="error" v-if="allowlistError">{{ allowlistError }}</div>

      <div class="muted" style="margin-top: 6px">
        Roots: <b>{{ allowlist.roots.length }}</b>
      </div>

      <div
        class="tableWrap"
        v-if="allowlist.roots.length"
        style="margin-top: 12px"
      >
        <table>
          <colgroup>
            <col style="width: 60px" />
            <col style="width: auto" />
            <col style="width: 160px" />
          </colgroup>

          <thead>
            <tr>
              <th>#</th>
              <th>Root</th>
              <th>İşlem</th>
            </tr>
          </thead>

          <tbody>
            <tr v-for="(r, i) in allowlist.roots" :key="r + '_' + i">
              <td>{{ i + 1 }}</td>

              <!-- Edit: inline -->
              <td>
                <input
                  class="text"
                  v-model="allowlist.roots[i]"
                  @input="allowDirty = true"
                />
              </td>

              <td>
                <button
                  class="danger"
                  :disabled="allowlistBusy"
                  @click="deleteRoot(r)"
                >
                  Sil
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else class="muted" style="margin-top: 10px">Root listesi boş.</div>

      <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 16px 0" />

      <!-- Optional: RegexFlags + SkipIdentifiers (şimdilik sadece gösterelim) -->
      <div class="row" style="gap: 20px; align-items: flex-start">
        <div style="min-width: 320px">
          <div style="font-weight: 600; margin-bottom: 6px">RegexFlags</div>
          <div class="muted" v-if="!allowlist.regexFlags.length">Boş</div>
          <ul v-else class="plainList">
            <li v-for="(x, idx) in allowlist.regexFlags" :key="'rf_' + idx">
              <code>{{ x }}</code>
            </li>
          </ul>
        </div>

        <div style="min-width: 320px">
          <div style="font-weight: 600; margin-bottom: 6px">
            SkipIdentifiers
          </div>
          <div class="muted" v-if="!allowlist.skipIdentifiers.length">Boş</div>
          <ul v-else class="plainList">
            <li
              v-for="(x, idx) in allowlist.skipIdentifiers"
              :key="'sk_' + idx"
            >
              <code>{{ x }}</code>
            </li>
          </ul>
        </div>

        <div style="min-width: 240px">
          <div style="font-weight: 600; margin-bottom: 6px">MaxUploadMb</div>
          <input
            class="text"
            type="number"
            min="1"
            max="90"
            v-model.number="allowlist.maxUploadMb"
            @input="allowDirty = true"
          />
          <div class="muted" style="margin-top: 6px">(Kaydet ile PUT)</div>

          <button
            class="secondary"
            :disabled="allowlistBusy || !allowDirty"
            @click="saveAllowlist"
          >
            Kaydet (PUT)
          </button>
        </div>
      </div>
    </section>

    <section class="card">
      <h2>1) XML Yükle</h2>

      <div class="row">
        <input type="file" accept=".xml" @change="onFileChange" />
        <button
          class="primary"
          :disabled="!file || uploading || running"
          @click="startAnalyze"
        >
          Analiz Et
        </button>
      </div>

      <div v-if="file" class="muted">
        Seçilen: <b>{{ file.name }}</b> ({{ formatBytes(file.size) }})
      </div>

      <div v-if="uploading" class="progress">
        <div class="bar" :style="{ width: uploadProgress + '%' }"></div>
      </div>
      <div v-if="uploading" class="muted">Upload: {{ uploadProgress }}%</div>

      <div v-if="error" class="error">
        {{ error }}
      </div>
    </section>

    <section class="card" v-if="jobId">
      <h2>2) Job Durumu</h2>

      <div class="status">
        <div><b>JobId:</b> {{ jobId }}</div>
        <div class="status-line">
          <span class="status-label">Status:</span>
          <span :class="['status', jobStatus.toLowerCase()]">
            {{ jobStatus }}
          </span>
        </div>
        <div><b>Progress:</b> {{ jobProgress }}%</div>
        <div class="muted" v-if="jobMessage">{{ jobMessage }}</div>
        <div class="error" v-if="jobError">{{ jobError }}</div>
      </div>

      <div class="row" style="margin-top: 12px">
        <button :disabled="!hasLog" @click="downloadLog">Log indir</button>
        <button :disabled="jobStatus !== 'Done'" @click="refreshIssues">
          Issue’ları yenile
        </button>
      </div>
    </section>

    <section class="card" v-if="jobStatus === 'Done'">
      <h2>3) Issue Listesi</h2>

      <div
        class="row"
        style="justify-content: space-between; align-items: center"
      >
        <div class="muted">
          Toplam: <b>{{ totalIssues }}</b> • Sayfa: <b>{{ page }}</b>
        </div>

        <div class="row" style="gap: 8px">
          <button :disabled="page <= 1" @click="goPage(page - 1)">
            Önceki
          </button>
          <button
            :disabled="page * pageSize >= totalIssues"
            @click="goPage(page + 1)"
          >
            Sonraki
          </button>
        </div>
      </div>

      <div class="tableWrap" v-if="issues.length">
        <table>
          <colgroup>
            <col style="width: 140px" />
            <col style="width: 260px" />
            <col style="width: 70px" />
            <col style="width: 70px" />
            <col style="width: 320px" />
            <col style="width: 600px" />
          </colgroup>
          <thead>
            <tr>
              <th>Rule</th>
              <th>Script</th>
              <th>Line</th>
              <th>Col</th>
              <th>Message</th>
              <th>Snippet</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(it, idx) in issues" :key="idx">
              <td>
                <code>{{ it.rule }}</code>
              </td>
              <td>{{ it.fullName }}</td>
              <td>{{ it.line }}</td>
              <td>{{ it.column }}</td>
              <td>{{ it.message }}</td>
              <td class="snippet">
                <code>{{ it.snippet }}</code>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-else class="muted">Issue yok 🎉</div>
    </section>

    <section class="card" v-if="systemTestResults.length || systemTestError">
      <h2>Sistem Testi Sonuçları</h2>
      <div class="muted" v-if="systemTestRanAt">
        Çalıştırma zamanı: {{ systemTestRanAt }}
      </div>
      <div class="error" v-if="systemTestError">
        {{ systemTestError }}
      </div>
      <ul class="system-test-list" v-if="systemTestResults.length">
        <li v-for="(check, idx) in systemTestResults" :key="idx">
          <span
            :class="[
              'system-test-badge',
              check.success ? 'system-test-pass' : 'system-test-fail',
            ]"
          >
            {{ check.success ? "PASS" : "FAIL" }}
          </span>
          <div class="system-test-body">
            <div class="system-test-name">{{ check.name }}</div>
            <div class="muted">{{ check.message }}</div>
          </div>
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup>
import axios from "axios";
import { onBeforeUnmount, ref } from "vue";

const file = ref(null);
const error = ref("");

const uploading = ref(false);
const uploadProgress = ref(0);

const jobId = ref("");
const jobStatus = ref("");
const jobProgress = ref(0);
const jobMessage = ref("");
const jobError = ref("");
const hasLog = ref(false);

const issues = ref([]);
const totalIssues = ref(0);
const page = ref(1);
const pageSize = ref(100);

const systemTestRunning = ref(false);
const systemTestResults = ref([]);
const systemTestError = ref("");
const systemTestRanAt = ref("");

let pollTimer = null;

function onFileChange(e) {
  error.value = "";
  const f = e.target.files?.[0];
  if (!f) {
    file.value = null;
    return;
  }
  if (f.size > 90 * 1024 * 1024) {
    error.value = "Dosya çok büyük. Max 90MB.";
    file.value = null;
    return;
  }
  file.value = f;
}

async function startAnalyze() {
  if (!file.value) return;

  // reset UI state
  error.value = "";
  jobError.value = "";
  issues.value = [];
  totalIssues.value = 0;
  page.value = 1;

  uploading.value = true;
  uploadProgress.value = 0;

  try {
    const form = new FormData();
    form.append("file", file.value);

    const res = await axios.post("/api/analyze", form, {
      headers: { "Content-Type": "multipart/form-data" },
      onUploadProgress: (p) => {
        const total = p.total || file.value.size || 1;
        uploadProgress.value = Math.min(
          100,
          Math.round((p.loaded * 100) / total),
        );
      },
    });

    jobId.value = res.data.jobId;
    uploading.value = false;

    await pollJob(); // ilk anlık durum
    startPolling(); // sonra düzenli polling
  } catch (e) {
    uploading.value = false;
    error.value = extractErr(e) || "Upload/Analyze sırasında hata.";
  }
}

function startPolling() {
  stopPolling();
  pollTimer = setInterval(async () => {
    await pollJob();
    // done ise polling durdurup issues çek
    if (jobStatus.value === "Done" || jobStatus.value === "Error") {
      stopPolling();
      if (jobStatus.value === "Done") await refreshIssues();
    }
  }, 1000);
}

function stopPolling() {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

async function pollJob() {
  if (!jobId.value) return;
  const res = await axios.get(`/api/jobs/${jobId.value}`);
  jobStatus.value = res.data.status;
  jobProgress.value = res.data.progress;
  jobMessage.value = res.data.message || "";
  jobError.value = res.data.error || "";
  hasLog.value = !!res.data.hasLog;
}

async function refreshIssues() {
  await fetchIssues(page.value);
}

async function goPage(p) {
  page.value = p;
  await fetchIssues(p);
}

async function fetchIssues(p) {
  if (!jobId.value) return;
  const res = await axios.get(`/api/jobs/${jobId.value}/issues`, {
    params: { page: p, pageSize: pageSize.value },
  });

  totalIssues.value = res.data.total;
  issues.value = res.data.items || [];
}

async function downloadLog() {
  if (!jobId.value) return;

  const res = await axios.get(`/api/jobs/${jobId.value}/log`, {
    responseType: "blob",
  });

  const blob = new Blob([res.data], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `webBasicCWFixer_${jobId.value}.log`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

function formatBytes(bytes) {
  const units = ["B", "KB", "MB", "GB"];
  let b = bytes;
  let u = 0;
  while (b >= 1024 && u < units.length - 1) {
    b /= 1024;
    u++;
  }
  return `${b.toFixed(u === 0 ? 0 : 1)} ${units[u]}`;
}

function extractErr(e) {
  // axios error
  const msg = e?.response?.data?.detail || e?.response?.data || e?.message;
  if (typeof msg === "string") return msg;
  try {
    return JSON.stringify(msg);
  } catch {
    return null;
  }
}

async function runSystemTest() {
  systemTestRunning.value = true;
  systemTestError.value = "";
  try {
    const res = await axios.post("/api/system-test");
    systemTestResults.value = res.data.checks || [];
    systemTestRanAt.value = res.data.ranAt
      ? new Date(res.data.ranAt).toLocaleString()
      : "";
  } catch (e) {
    systemTestError.value = extractErr(e) || "Sistem testi başarısız.";
  } finally {
    systemTestRunning.value = false;
  }
}

onBeforeUnmount(() => stopPolling());
// -------------------- Allowlist UI --------------------
const showAllowlist = ref(false);
const allowlistBusy = ref(false);
const allowlistError = ref("");
const allowDirty = ref(false);

const newRoot = ref("");

const allowlist = ref({
  roots: [],
  regexFlags: [],
  skipIdentifiers: [],
  maxUploadMb: 90,
});

function toggleAllowlist() {
  showAllowlist.value = !showAllowlist.value;
  if (showAllowlist.value) {
    reloadAllowlist();
  }
}

async function reloadAllowlist() {
  allowlistError.value = "";
  allowlistBusy.value = true;
  allowDirty.value = false;

  try {
    const res = await axios.get("/api/allowlist");
    allowlist.value = normalizeAllowlist(res.data);

    // Roots'u UI tarafında her zaman sıralı gösterelim (istersen kaldır)
    allowlist.value.roots.sort((a, b) => a.localeCompare(b));
  } catch (e) {
    allowlistError.value = extractErr(e) || "Allowlist alınamadı.";
  } finally {
    allowlistBusy.value = false;
  }
}

async function addRoot() {
  const v = newRoot.value.trim();
  if (!v) return;

  allowlistError.value = "";
  allowlistBusy.value = true;

  try {
    const res = await axios.post("/api/allowlist/roots", { value: v });
    allowlist.value = normalizeAllowlist(res.data);
    allowlist.value.roots.sort((a, b) => a.localeCompare(b));
    newRoot.value = "";
    allowDirty.value = false;
  } catch (e) {
    allowlistError.value = extractErr(e) || "Root eklenemedi.";
  } finally {
    allowlistBusy.value = false;
  }
}

async function deleteRoot(value) {
  const v = (value || "").trim();
  if (!v) return;

  allowlistError.value = "";
  allowlistBusy.value = true;
  console.log("deleteRoot called", { value, v });

  try {
    //const url = `/api/allowlist/roots/${encodeURIComponent(v)}`;
    const res = await axios.delete("/api/allowlist/roots", {
      params: { value: v },
    });

    allowlist.value = normalizeAllowlist(res.data);
    allowlist.value.roots.sort((a, b) => a.localeCompare(b));
    allowDirty.value = false;
  } catch (e) {
    allowlistError.value = extractErr(e) || "Root silinemedi.";
  } finally {
    allowlistBusy.value = false;
  }
}

async function saveAllowlist() {
  allowlistError.value = "";
  allowlistBusy.value = true;

  try {
    // trim + unique + boşları at
    const cleanedRoots = (allowlist.value.roots || [])
      .map((x) => (x ?? "").trim())
      .filter((x) => x.length > 0);

    // uniq (ordinal)
    const uniq = [];
    const seen = new Set();
    for (const x of cleanedRoots) {
      if (!seen.has(x)) {
        seen.add(x);
        uniq.push(x);
      }
    }

    const dto = {
      roots: uniq,
      regexFlags: allowlist.value.regexFlags || [],
      skipIdentifiers: allowlist.value.skipIdentifiers || [],
      maxUploadMb: allowlist.value.maxUploadMb || 90,
    };

    const res = await axios.put("/api/allowlist", dto);
    allowlist.value = normalizeAllowlist(res.data);
    allowlist.value.roots.sort((a, b) => a.localeCompare(b));
    allowDirty.value = false;
  } catch (e) {
    allowlistError.value = extractErr(e) || "Allowlist kaydedilemedi.";
  } finally {
    allowlistBusy.value = false;
  }
}

// backend casing farklarını normalize et
function normalizeAllowlist(data) {
  const d = data || {};
  return {
    roots: d.roots ?? d.Roots ?? [],
    regexFlags: d.regexFlags ?? d.RegexFlags ?? [],
    skipIdentifiers: d.skipIdentifiers ?? d.SkipIdentifiers ?? [],
    maxUploadMb: d.maxUploadMb ?? d.MaxUploadMb ?? 90,
  };
}
</script>
