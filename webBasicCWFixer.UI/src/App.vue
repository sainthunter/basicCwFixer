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

    <UploadCard
      :file="file"
      :uploading="uploading"
      :upload-progress="uploadProgress"
      :error="error"
      :on-file-change="onFileChange"
      :on-start-analyze="startAnalyze"
      :format-bytes="formatBytes"
    />

    <JobStatusCard
      :job-id="jobId"
      :job-status="jobStatus"
      :job-progress="jobProgress"
      :job-message="jobMessage"
      :job-error="jobError"
      :has-log="hasLog"
      :on-download-log="downloadLog"
      :on-refresh-issues="refreshIssues"
      :migration-ready="migrationReady"
      :on-open-migrations="openMigrationModal"
    />

    <IssueListCard
      :is-visible="jobStatus === 'Done'"
      :issues="issues"
      :total-issues="totalIssues"
      :page="page"
      :page-size="pageSize"
      :can-next="canNextPage"
      :on-prev-page="() => goPage(page - 1)"
      :on-next-page="() => goPage(page + 1)"
      :on-page-size-change="onPageSizeChange"
    />

    <SystemTestPanel
      :results="systemTestResults"
      :error="systemTestError"
      :ran-at="systemTestRanAt"
    />

    <div v-if="migrationModalOpen" class="modal-backdrop">
      <div class="modal">
        <div class="modal-header">
          <h3>Migration Findings</h3>
          <button class="modal-close" @click="closeMigrationModal">Kapat</button>
        </div>
        <div class="muted" v-if="migrationMeta">
          Toplam: {{ migrationMeta.total }}
        </div>
        <div class="error" v-if="migrationError">{{ migrationError }}</div>
        <div class="tableWrap" v-if="migrationItems.length">
          <table>
            <colgroup>
              <col style="width: 280px" />
              <col style="width: 140px" />
              <col style="width: 220px" />
              <col style="width: 160px" />
              <col style="width: 120px" />
              <col style="width: 260px" />
              <col style="width: 220px" />
            </colgroup>
            <thead>
              <tr>
                <th>Parent</th>
                <th>RefType</th>
                <th>Target</th>
                <th>Expected</th>
                <th>Severity</th>
                <th>Location</th>
                <th>Reason</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(item, idx) in migrationItems" :key="idx">
                <td>{{ item.parentProcessName }}</td>
                <td>{{ item.refType }}</td>
                <td>{{ item.targetProcessName }}</td>
                <td>{{ item.expectedVersion }}</td>
                <td>{{ item.severity }}</td>
                <td>{{ item.location }}</td>
                <td>{{ item.reason }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-else class="muted" v-if="!migrationLoading">
          Migration bulgusu yok.
        </div>
        <div class="muted" v-if="migrationLoading">Yükleniyor...</div>
      </div>
    </div>
  </div>
</template>

<script setup>
import axios from "axios";
import { computed, onBeforeUnmount, ref } from "vue";
import IssueListCard from "./components/IssueListCard.vue";
import JobStatusCard from "./components/JobStatusCard.vue";
import SystemTestPanel from "./components/SystemTestPanel.vue";
import UploadCard from "./components/UploadCard.vue";

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
const migrationReady = ref(false);

const issues = ref([]);
const totalIssues = ref(0);
const page = ref(1);
const pageSize = ref(loadPageSize());

const systemTestRunning = ref(false);
const systemTestResults = ref([]);
const systemTestError = ref("");
const systemTestRanAt = ref("");

const migrationModalOpen = ref(false);
const migrationItems = ref([]);
const migrationMeta = ref(null);
const migrationError = ref("");
const migrationLoading = ref(false);

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
  migrationReady.value = !!res.data.migrationReady;
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

function onPageSizeChange(e) {
  const value = Number.parseInt(e?.target?.value, 10);
  if (!Number.isFinite(value)) return;
  const clamped = Math.min(500, Math.max(1, value));
  pageSize.value = clamped;
  savePageSize(clamped);
  page.value = 1;
  if (jobId.value) {
    fetchIssues(page.value);
  }
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

async function openMigrationModal() {
  migrationModalOpen.value = true;
  await fetchMigrations();
}

function closeMigrationModal() {
  migrationModalOpen.value = false;
}

async function fetchMigrations() {
  if (!jobId.value) return;
  migrationLoading.value = true;
  migrationError.value = "";
  try {
    const res = await axios.get(`/api/jobs/${jobId.value}/migrations`, {
      params: { limit: 200 },
    });
    migrationItems.value = res.data.items || [];
    migrationMeta.value = { total: res.data.total || 0 };
  } catch (e) {
    migrationError.value = extractErr(e) || "Migration sonuçları alınamadı.";
  } finally {
    migrationLoading.value = false;
  }
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

const canNextPage = computed(
  () => page.value * pageSize.value < totalIssues.value,
);

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

function loadPageSize() {
  if (typeof window === "undefined") return 100;
  const stored = window.localStorage.getItem("issuePageSize");
  const parsed = Number.parseInt(stored || "", 10);
  if (!Number.isFinite(parsed)) return 100;
  return Math.min(500, Math.max(1, parsed));
}

function savePageSize(size) {
  if (typeof window === "undefined") return;
  window.localStorage.setItem("issuePageSize", String(size));
}
</script>
