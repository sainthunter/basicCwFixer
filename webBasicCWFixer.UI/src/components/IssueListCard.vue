<template>
  <section class="card" v-if="isVisible">
    <h2>3) Issue Listesi</h2>

    <div class="row" style="justify-content: space-between; align-items: center">
      <div class="muted">
        Toplam: <b>{{ totalIssues }}</b> • Sayfa: <b>{{ page }}</b>
      </div>

      <div class="row" style="gap: 8px">
        <label class="row" style="gap: 6px">
          <span class="muted">Sayfa Boyutu</span>
          <input
            class="page-size-input"
            type="number"
            min="1"
            max="500"
            :value="pageSize"
            @change="onPageSizeChange"
          />
        </label>
        <button :disabled="page <= 1" @click="onPrevPage">Önceki</button>
        <button :disabled="!canNext" @click="onNextPage">Sonraki</button>
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
</template>

<script setup>
defineProps({
  isVisible: {
    type: Boolean,
    required: true,
  },
  issues: {
    type: Array,
    required: true,
  },
  totalIssues: {
    type: Number,
    required: true,
  },
  page: {
    type: Number,
    required: true,
  },
  pageSize: {
    type: Number,
    required: true,
  },
  canNext: {
    type: Boolean,
    required: true,
  },
  onPrevPage: {
    type: Function,
    required: true,
  },
  onNextPage: {
    type: Function,
    required: true,
  },
  onPageSizeChange: {
    type: Function,
    required: true,
  },
});
</script>
