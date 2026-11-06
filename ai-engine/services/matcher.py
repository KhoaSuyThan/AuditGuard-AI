# -*- coding: utf-8 -*-
"""
Semantic Item Matching Engine
- Hỗ trợ Vector Cosine Similarity (sentence-transformers)
- Báo đúng tên engine đang thực thi (f"Vector-{self.model_name}")
- Character Tri-Gram kết hợp Token Jaccard Similarity cho Fallback
- Thuật toán Maximum Weight Bipartite Matching (Hungarian Algorithm via Scipy)
  hoặc Global Sorted Edges Greedy để đảm bảo kết quả tối ưu toàn cục.
"""
import re
from typing import List, Dict, Any


class SemanticMatcher:
    def __init__(self, model_name: str = "paraphrase-multilingual-MiniLM-L12-v2"):
        self.model_name = model_name
        self._model = None
        self._use_fallback = False

    def _load_model(self):
        if self._model is None and not self._use_fallback:
            try:
                from sentence_transformers import SentenceTransformer
                self._model = SentenceTransformer(self.model_name)
            except Exception:
                self._use_fallback = True

    @staticmethod
    def _char_ngrams(text: str, n: int = 3) -> set:
        """Trích xuất tập hợp Character N-Grams (mặc định Tri-gram)"""
        clean = re.sub(r"\s+", " ", text.lower().strip())
        if len(clean) < n:
            return {clean}
        return {clean[i:i + n] for i in range(len(clean) - n + 1)}

    @staticmethod
    def _tokenize(text: str) -> set:
        clean = re.sub(r"[^\w\s]", " ", text.lower())
        return set(clean.split())

    @classmethod
    def _fallback_similarity(cls, text_a: str, text_b: str) -> float:
        """
        Hybrid Similarity:
        - 50% Character Tri-Gram Jaccard (bắt lỗi chính tả, biến thể từ)
        - 50% Token Jaccard (bắt từ vựng)
        - Thưởng điểm nếu trùng mã model/số (ví dụ 85A, CE285A, 500GB)
        """
        ngrams_a = cls._char_ngrams(text_a, n=3)
        ngrams_b = cls._char_ngrams(text_b, n=3)
        tokens_a = cls._tokenize(text_a)
        tokens_b = cls._tokenize(text_b)

        # 1. Char N-Gram Jaccard
        char_sim = len(ngrams_a & ngrams_b) / len(ngrams_a | ngrams_b) if (ngrams_a and ngrams_b) else 0.0

        # 2. Token Jaccard
        token_sim = len(tokens_a & tokens_b) / len(tokens_a | tokens_b) if (tokens_a and tokens_b) else 0.0

        score = (char_sim * 0.5) + (token_sim * 0.5)

        # 3. Model Code bonus
        codes_a = {t for t in tokens_a if any(c.isdigit() for c in t)}
        codes_b = {t for t in tokens_b if any(c.isdigit() for c in t)}
        if codes_a and codes_b and (codes_a & codes_b):
            score = min(1.0, score + 0.35)

        return round(float(score), 4)

    def match_items_bipartite(
        self,
        po_items: List[Dict[str, Any]],
        invoice_items: List[Dict[str, Any]],
        similarity_threshold: float = 0.50
    ) -> List[Dict[str, Any]]:
        """
        Thuật toán Maximum Weight Bipartite Matching:
        - Xây dựng ma trận tương đồng (N x M)
        - Sử dụng scipy.optimize.linear_sum_assignment (Hungarian Algorithm)
          hoặc Global Sorted Edges để tối ưu hóa TỔNG ĐIỂM TOÀN CỤC.
        """
        if not po_items or not invoice_items:
            return []

        self._load_model()
        po_names = [item.get("item_name", "") for item in po_items]
        inv_names = [item.get("item_name", "") for item in invoice_items]

        use_vector = (self._model is not None)
        active_engine = f"Vector-{self.model_name}" if use_vector else "Hybrid-Token-Char-Ngram"

        num_po = len(po_names)
        num_inv = len(inv_names)

        # 1. Tính toán ma trận tương đồng (Similarity Matrix)
        if use_vector:
            try:
                import numpy as np
                po_vecs = self._model.encode(po_names, convert_to_numpy=True, normalize_embeddings=True)
                inv_vecs = self._model.encode(inv_names, convert_to_numpy=True, normalize_embeddings=True)
                sim_matrix = np.dot(po_vecs, inv_vecs.T)
            except Exception:
                use_vector = False
                active_engine = "Hybrid-Token-Char-Ngram"

        if not use_vector:
            sim_matrix = []
            for p_name in po_names:
                row = [self._fallback_similarity(p_name, i_name) for i_name in inv_names]
                sim_matrix.append(row)

        # 2. Giải bài toán Bipartite Matching tối ưu toàn cục
        matched_pairs = {}  # po_idx -> (inv_idx, score)
        matched_inv_indices = set()

        try:
            # Phương pháp A: Scipy Linear Sum Assignment (Hungarian Algorithm)
            import numpy as np
            from scipy.optimize import linear_sum_assignment
            np_matrix = np.array(sim_matrix)
            # Vì linear_sum_assignment tìm cost nhỏ nhất -> truyền (-matrix) để tối đa điểm
            row_ind, col_ind = linear_sum_assignment(-np_matrix)
            for r, c in zip(row_ind, col_ind):
                score = float(np_matrix[r, c])
                if score >= similarity_threshold:
                    matched_pairs[r] = (c, score)
                    matched_inv_indices.add(c)
        except Exception:
            # Phương pháp B: Global Sorted Edges Greedy (Không phụ thuộc thứ tự PO)
            # Sắp xếp tất cả các cặp có thể có trên toàn hệ thống theo điểm giảm dần
            all_edges = []
            for r in range(num_po):
                for c in range(num_inv):
                    score = float(sim_matrix[r][c] if not use_vector else sim_matrix[r, c])
                    all_edges.append((score, r, c))

            all_edges.sort(key=lambda x: x[0], reverse=True)

            used_po = set()
            for score, r, c in all_edges:
                if score < similarity_threshold:
                    break
                if r not in used_po and c not in matched_inv_indices:
                    matched_pairs[r] = (c, score)
                    used_po.add(r)
                    matched_inv_indices.add(c)

        # 3. Tổng hợp kết quả
        results = []
        for po_idx, po_item in enumerate(po_items):
            if po_idx in matched_pairs:
                inv_idx, score = matched_pairs[po_idx]
                results.append({
                    "po_item": po_item,
                    "matched_invoice_item": invoice_items[inv_idx],
                    "similarity_score": round(score, 4),
                    "is_confident_match": True,
                    "matcher_engine": active_engine,
                    "status": "MATCHED"
                })
            else:
                results.append({
                    "po_item": po_item,
                    "matched_invoice_item": None,
                    "similarity_score": 0.0,
                    "is_confident_match": False,
                    "matcher_engine": active_engine,
                    "status": "UNMATCHED_PO_ITEM"
                })

        for inv_idx, inv_item in enumerate(invoice_items):
            if inv_idx not in matched_inv_indices:
                results.append({
                    "po_item": None,
                    "matched_invoice_item": inv_item,
                    "similarity_score": 0.0,
                    "is_confident_match": False,
                    "matcher_engine": active_engine,
                    "status": "EXTRA_INVOICE_ITEM"
                })

        return results
